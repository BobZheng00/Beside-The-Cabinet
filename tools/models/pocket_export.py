"""Exports a Pocket TTS model to the five ONNX files and two tokenizer files the app runs (InsertCoin.Mind's PocketTts; sherpa-onnx
loads them too), so her voice can run a newer model than the public January export (#280). The English export is hosted on this
repository's release pocket-tts-en-1, which tools/models/fetch.py pins. The weights come from where Pocket TTS keeps them on this
PC: nothing is downloaded by this script.

    <Pocket TTS's python> -I tools/models/pocket_export.py export <english|config.yaml> <folder>
    <a python with onnxruntime> -I tools/models/pocket_export.py quantize <folder>

The first runs in Pocket TTS's own environment (it needs no `onnx` package); the second writes the *.int8.onnx files.

What the export adds to the model, because sherpa-onnx 1.13.8 doesn't do these itself:
- encoder.onnx puts the model's start-of-voice embedding (`insert_bos_before_voice`) in front of the voice;
- lm_main.onnx holds the end-of-speech signal back for the first frames, as Pocket TTS's own loop does;
- decoder.onnx undoes the latents' normalisation.
Its state starts from zeros and grows with the sample and the sentence, so no sample length is built in. What's left for the caller:
end the sample on a short pause (Pocket TTS's end_on_pause), and prepare the text (first letter, last full stop, replace_characters).
"""
import json
import sys
from pathlib import Path
from types import MethodType


class GrowingCache:
    """Takes the place of Pocket TTS's fixed-size attention cache: an ONNX graph can't write into a tensor at a moving position, so
    the keys and values are joined on the end instead, and cut to the attention's context when it has one."""

    requires_state = True

    def __init__(self, context):
        self.context = context

    def rope_offset(self, state, batch_size, device):
        return state["offset"].view(-1)[0]

    def append_and_get(self, k, v, state):
        import torch

        cache, offset = state["cache"], state["offset"].view(-1)[0]
        stored = cache.shape[2]
        cache = cache[:, :, stored - torch.min(torch.as_tensor(stored), offset):]  # at offset 0 the zeros the runtime starts from go
        keys, values = torch.cat([cache[0], k], dim=1), torch.cat([cache[1], v], dim=1)
        whole, added = keys.shape[1], k.shape[1]
        positions = torch.arange(whole, dtype=torch.long).view(1, -1) + offset + added - whole
        state["offset"] = state["offset"] + added
        kept = torch.stack([keys, values])
        state["cache"] = kept[:, :, -self.context:] if self.context else kept
        return keys.permute(0, 2, 1, 3), values.permute(0, 2, 1, 3), positions, state["offset"] - added


def tokenizer_files(tokenizer, folder):
    """vocab.json and token_scores.json, as sherpa-onnx's SentencePieceTokenizer reads them."""
    if hasattr(tokenizer, "sp"):
        pieces = [(tokenizer.sp.id_to_piece(i), tokenizer.sp.get_score(i)) for i in range(tokenizer.sp.get_piece_size())]
    else:
        pieces = json.loads(tokenizer.tokenizer.to_str())["model"]["vocab"]
        assert all(len(p) == 2 for p in pieces), "expected a SentencePiece vocabulary with scores"
    (folder / "vocab.json").write_text(json.dumps({p: i for i, (p, _) in enumerate(pieces)}, ensure_ascii=False, indent=1), encoding="utf-8")
    (folder / "token_scores.json").write_text(json.dumps({p: s for p, s in pieces}, ensure_ascii=False, indent=1), encoding="utf-8")


def export(which, folder):
    import torch
    from pocket_tts import TTSModel
    from pocket_tts.modules import attention, transformer
    from pocket_tts.modules.stateful_module import StatefulModule
    from torch import nn
    from torch.nn import functional as F
    from torch.onnx._internal.torchscript_exporter import onnx_proto_utils

    # shortcut: this hook only adds onnx-script functions, which this model has none of, and it is all that wants the `onnx` package
    onnx_proto_utils._add_onnxscript_fn = lambda proto, _: proto

    transformer._cached_causal_mask = lambda t, context, device: None  # the encoder's windowed attention makes its own; this one is cached by size, which a trace would freeze

    def windowed(self, query, model_state, attn_mask=None):
        """The encoder's attention without a whole-sample matrix: a sample of half a minute is 6,000 steps, and one attention over all of them
        takes 10 GB in ONNX Runtime. Queries go in blocks of the attention's context, each against its own block and the one before it."""
        h, d, c = self.num_heads, self.dim_per_head, self.context
        assert self.num_kv_heads == h and c is not None, "the encoder has no grouped-query attention"
        projected = self.in_proj(query)
        b, t, _ = projected.shape
        q = projected[..., : self.attn_dim].view(b, t, h, d)
        k, v = torch.unbind(projected[..., self.attn_dim :].view(b, t, 2, h, d), dim=2)
        q, k = self.rope(q, k, offset=torch.zeros((), dtype=torch.long))
        extra = (-t) % c
        blocks = torch.div(t + extra, c, rounding_mode="floor")

        def cut(x):
            return torch.cat([x, x.new_zeros(1, extra, h, d)], dim=1).view(blocks, c, h, d)

        def with_previous(x):
            return torch.cat([torch.cat([x.new_zeros(1, c, h, d), x[:-1]], dim=0), x], dim=1)

        q, keys, values = cut(q), with_previous(cut(k)), with_previous(cut(v))
        start = torch.arange(blocks, dtype=torch.long).view(-1, 1) * c
        pos_q = start + torch.arange(c, dtype=torch.long).view(1, -1)
        pos_k = start - c + torch.arange(2 * c, dtype=torch.long).view(1, -1)
        mask = attention._build_attention_mask(pos_q, pos_k, c) & (pos_k[:, None, None, :] < t)
        x = F.scaled_dot_product_attention(q.transpose(1, 2), keys.transpose(1, 2), values.transpose(1, 2), mask, dropout_p=0.0)
        return self.out_proj(x.transpose(1, 2).reshape(1, -1, h * d)[:, :t])

    model = TTSModel.load_model(config=which) if which.endswith((".yaml", ".yml")) else TTSModel.load_model(language=which)
    assert model.has_voice_cloning, "these weights can't clone a voice"
    flow, mimi = model.flow_lm, model.mimi
    assert flow.flow_type == "lsd" and flow.z_proj is None, "sherpa-onnx's loop only does the two-time LSD flow"
    decoding = ("upsample", "decoder_transformer", "decoder.")

    def layout(root, wanted=lambda name: True):
        """The modules' state: a template, and which of its tensors go in and out of the graph, in a fixed order. The rest stay as they start."""
        template, inputs = {}, []
        for name, module in root.named_modules():
            if isinstance(module, StatefulModule) and wanted(name):
                template[name] = module.init_state(1, sequence_length=1)
                inputs += [(name, key) for key, value in template[name].items() if key not in ("first", "pad") and value.numel()]
        return template, inputs

    def fill(template, inputs, values):
        state = {name: dict(parts) for name, parts in template.items()}
        for (name, key), value in zip(inputs, values):
            state[name][key] = value.clone()  # the modules write into their state
        return state

    class Part(nn.Module):
        def __init__(self):  # the models as submodules, so their weights are the graph's initializers, which quantisation needs
            super().__init__()
            self.flow, self.mimi = flow, mimi

    class TextConditioner(Part):
        def forward(self, token_ids):
            return flow.conditioner(token_ids)

    class Encoder(Part):
        def forward(self, audio):
            short = (-audio.shape[-1]) % mimi.frame_size
            audio = torch.cat([audio, audio.new_zeros(1, 1, short)], dim=-1)
            latents, = mimi.encoder_transformer(mimi.encoder(audio, None), None)
            voice = F.linear(mimi._to_framerate(latents).transpose(-1, -2).float(), flow.speaker_proj_weight)
            return torch.cat([flow.bos_before_voice, voice], dim=1) if flow.insert_bos_before_voice else voice

    class LmFlow(Part):
        def forward(self, c, s, t, x):  # on three dimensions, so its layers are the MatMul that dynamic quantisation handles
            return flow.flow_net(c[:, None], s[:, None], t[:, None], x[:, None])[:, 0]

    main_template, main_state = layout(flow)

    class LmMain(Part):
        def forward(self, sequence, text_embeddings, *values):
            state = fill(main_template, main_state, values[:-1])
            frames = values[-1]
            embedded = flow.input_linear(torch.where(torch.isnan(sequence), flow.bos_emb, sequence))
            out = flow.out_norm(flow.transformer(torch.cat([text_embeddings, embedded], dim=1), state)).float()[:, -1]
            eos = torch.where(frames < model._MIN_FRAMES_BEFORE_EOS, torch.tensor(-100.0), flow.out_eos(out))
            return (out, eos, *[state[n][k] for n, k in main_state], frames + sequence.shape[1])

    decoder_template, decoder_state = layout(mimi, lambda name: name.startswith(decoding))
    for name, module in list(flow.named_modules()) + list(mimi.named_modules()):
        if isinstance(module, attention.StreamingMultiheadAttention):
            if name.startswith("encoder_transformer"):
                module.forward = MethodType(windowed, module)
            else:
                module._cache_backend = GrowingCache(module.context)

    class Decoder(Part):
        def forward(self, latent, *values):
            state = fill(decoder_template, decoder_state, values)
            emb = mimi._to_encoder_framerate(mimi.quantizer((latent * flow.emb_std + flow.emb_mean).transpose(-1, -2)), state)
            emb, = mimi.decoder_transformer(emb, state)
            return (mimi.decoder(emb, state), *[state[n][k] for n, k in decoder_state])

    def save(module, name, args, inputs, outputs, axes):
        with torch.no_grad():
            torch.onnx.export(module.eval(), args, str(folder / name), input_names=inputs, output_names=outputs, dynamic_axes=axes, opset_version=17, dynamo=False)
        print(name, (folder / name).stat().st_size, flush=True)

    def state_axes(values, names):
        return {n: {2: f"cached_{i}"} for i, (v, n) in enumerate(zip(values, names)) if v.dim() == 5}

    def cached(template, inputs, length):
        """Values for the trace, of a state already holding `length` steps; only their shape matters."""
        values = []
        for name, key in inputs:
            value = template[name][key]
            if key == "cache":
                value = torch.zeros(value.shape[0], 1, length, *value.shape[3:])
            elif key == "offset":
                value = torch.tensor([length])
            else:
                value = torch.zeros_like(value)
            values.append(value)
        return values

    folder.mkdir(parents=True, exist_ok=True)
    tokenizer_files(flow.conditioner.tokenizer, folder)
    save(TextConditioner(), "text_conditioner.onnx", (torch.ones((1, 3), dtype=torch.long),), ["token_ids"], ["embeddings"], {"token_ids": {1: "seq_len"}, "embeddings": {1: "seq_len"}})
    save(Encoder(), "encoder.onnx", (torch.randn(1, 1, mimi.frame_size * 5 + 13),), ["audio"], ["latents"], {"audio": {2: "audio_len"}, "latents": {1: "frames"}})
    time = torch.ones(1, 1)
    save(LmFlow(), "lm_flow.onnx", (torch.randn(1, 1024), time, time, torch.randn(1, 32)), ["c", "s", "t", "x"], ["flow_dir"], {})

    values = cached(main_template, main_state, 4) + [torch.tensor([2])]
    names = [f"state_{i}" for i in range(len(values))]
    save(LmMain(), "lm_main.onnx", (torch.randn(1, 1, 32), torch.randn(1, 2, 1024), *values), ["sequence", "text_embeddings", *names], ["conditioning", "eos_logit", *[f"out_{n}" for n in names]],
         {"sequence": {1: "seq_len"}, "text_embeddings": {1: "text_len"}, **state_axes(values, names), **state_axes(values, [f"out_{n}" for n in names])})

    values = cached(decoder_template, decoder_state, 4)
    names = [f"state_{i}" for i in range(len(values))]
    save(Decoder(), "decoder.onnx", (torch.randn(1, 2, 32), *values), ["latent", *names], ["audio_frame", *[f"out_{n}" for n in names]],
         {"latent": {1: "seq_len"}, "audio_frame": {2: "samples"}, **state_axes(values, names), **state_axes(values, [f"out_{n}" for n in names])})


def quantize(folder):
    import onnx

    if not hasattr(onnx.TensorProto, "INT4"):  # shortcut: an older onnx beside a newer onnxruntime; these models have no 4-bit tensors
        onnx.TensorProto.INT4, onnx.TensorProto.UINT4 = 22, 21
    from onnxruntime.quantization import QuantType, quantize_dynamic

    for name in ("lm_main", "lm_flow", "decoder"):
        quantize_dynamic(str(folder / f"{name}.onnx"), str(folder / f"{name}.int8.onnx"), weight_type=QuantType.QUInt8)
        print(name, (folder / f"{name}.int8.onnx").stat().st_size, flush=True)


if __name__ == "__main__":
    if sys.argv[1] == "export":
        export(sys.argv[2], Path(sys.argv[3]))
    elif sys.argv[1] == "quantize":
        quantize(Path(sys.argv[2]))
    else:
        sys.exit(__doc__)
