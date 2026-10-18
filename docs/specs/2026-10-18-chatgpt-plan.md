# Her mind on the player's ChatGPT plan

*Decided with the user, 2026-10-18 (`/pm:brainstorm #87`). Nothing built yet; the order is at the end. What OpenAI
offers is from its developer docs as read that day (Sources): the builder reads them again before building, since the
feature is a preview.*

## Why

The user, 2026-10-18: "allow user to use chatgpt subscription to power the LLM chat". Today her mind needs an API key
(an OpenAI-compatible or Anthropic-style provider, kept by DPAPI in `KeyStore`) or a local model. Many players already
pay for ChatGPT Plus or Pro and have neither.

OpenAI made this possible at DevDay (29 September 2026): **Sign in with ChatGPT** with **plan usage** lets a
participating app run AI requests on the player's ChatGPT plan. At launch it is open to open-source projects and
personal projects that run locally, self-serve; paid or hosted apps join a waitlist. This app is open source and runs
locally, so the GitHub builds qualify now.

## What OpenAI offers (as read on 2026-10-18)

- **Sign-in:** OpenID Connect with PKCE, the system browser, a callback on `127.0.0.1` (loopback). An open-source
  client starts with `client_id=dynamic_agent_client`, an `agent_name_hint` and a host id made once per installation
  (`ext_agent_host_id`, a `urn:uuid:`); OpenAI issues the app's client id during the first sign-in. No client secret.
  Scopes: `openid profile email offline_access resource.invoke chatgpt.tokens.use.direct`, resource
  `https://api.openai.com/v1`. Plan usage is a separate consent from signing in: the session can use the plan only if
  that scope was granted.
- **Tokens:** an access token for an hour, a refresh token for 30 days, replaced on each refresh. A player who
  disconnects the app in ChatGPT is not announced: the next request or refresh fails.
- **Requests:** `POST https://api.openai.com/v1/responses` with the access token as a bearer token; `stream: true` and
  `store: false` on every request, the whole context sent each time. System message items are refused: `instructions`
  or developer messages instead. No `temperature`, `top_p`, `max_output_tokens`, `previous_response_id`,
  `background`, `user`. Function tools work; OpenAI's hosted tools (image generation, file search, Code Interpreter,
  hosted connectors, tool search) don't; web search depends on the model and account. Text, images and files in
  input work where the model takes them. Success is `response.completed` only.
- **Models:** `GET /v1/models` with the same token; those with `visibility == "list"`, by `slug`, shown by
  `display_name`. The list depends on the account.
- **Limits:** the player sets a weekly cap for each app in ChatGPT's settings (a share of their weekly usage); Plus
  also has a five-hour limit shared by every app; nothing is reserved. When a limit is reached a request fails (HTTP 429
  `subscription_sharing_usage_limit_exceeded` or `…_usage_unavailable`, possibly as `response.failed` mid-stream);
  OpenAI never switches to other billing. An account that isn't eligible gets 403
  `subscription_sharing_user_not_eligible`; a disconnected one 401 `subscription_sharing_invalid_user` or
  `invalid_grant` on refresh.
- **What the app must show:** a "Continue with ChatGPT" button; "Connected" once signed in; "Using ChatGPT plan" near
  the model; a **Manage usage** action that opens ChatGPT's Settings → Usage, the main action in a limit message.

## What it does

- **Settings → Her mind** gains **ChatGPT plan** as a provider. Choosing it shows **Continue with ChatGPT**: the
  player's browser opens, they sign in and allow plan usage, and the page shows "Connected as <email>", "Using
  ChatGPT plan", the model (from their account's list), **Manage usage** and **Disconnect**.
- **Everything she does runs on the plan:** her replies, her own check-ins and remarks, reminders she speaks, texts,
  and her memory upkeep. Settings' usage already shows what each purpose spent.
- **When the plan's limit is reached:**
  - with a **backup** set (an API key or a local model, chosen on the same page, optional), she **asks first**: a card
    by her stool, "The ChatGPT plan's limit is reached. Carry on with <backup> until it resets?", Yes / Not now, with
    Manage usage. Nothing is spent on the backup without a yes;
  - with no backup, or until the player answers, she says once, plainly, that the plan's limit is reached (with
    Manage usage), and waits: no replies, check-ins or remarks. **Reminders still come**, as plain tickets or texts in
    the app's words, which need no model;
  - she **goes back to the plan by herself** when it works again: she tries it now and then (every 15 minutes, to
    tune) and returns as soon as it answers, saying so once. The next time the plan runs out, she asks again.
- **Signed out** (a disconnect, an expired refresh, an account no longer eligible) is told the same plain way, with
  **Continue with ChatGPT** to sign in again; the backup rule above applies.
- **Getting started**'s step for her mind offers **Continue with ChatGPT** first ("if you have ChatGPT Plus or Pro"),
  and "Use an API key or a local model" as the other way.

## Decided

- **Everything on the plan** (the user, 2026-10-18, choosing "Everything"): one provider, her background work included;
  the player's weekly cap governs it.
- **A backup when the plan runs out** (the user, 2026-10-18, choosing "Fall back to a backup"), and **asked first**
  (choosing "Ask the player first"): a paid key is never spent without a yes.
- **Back to the plan by herself** when it works again (the user, 2026-10-18, choosing "By herself, when it works
  again").
- **With no backup, reminders still come, plainly**; check-ins and remarks wait (the user, 2026-10-18, choosing
  "Reminders still come, plainly").
- **Kept: the tokens and the account's email**, encrypted in `KeyStore`, to show "Connected as <email>"; never in
  settings.json or the log; **Disconnect** forgets them all (the user, 2026-10-18, choosing "Tokens, and the email to
  show"). The app's issued client id and the installation's host id are not secrets and may sit with the settings.
- **First in Getting started** (the user, 2026-10-18, choosing "First in Getting started").
- **Built now for the GitHub builds; OpenAI's waitlist before Steam** (the user, 2026-10-18, choosing "Build now,
  waitlist before Steam"): a Steam build hides ChatGPT plan until OpenAI approves it (`docs/publishing.md`, Steam).
- **No new dependency:** the OpenAI .NET library the app already uses has a Responses client; the sign-in is a few
  HTTP calls and a loopback listener.
- **Tests never use a real ChatGPT account** (CLAUDE.md: never the user's real keys): a stand-in sign-in server and
  Responses endpoint, as the other mock servers; the player's own plan is a manual check.

## Not built, and why

- **Claude, Gemini or other subscriptions:** Anthropic stopped third-party use of Claude subscriptions in February
  2026; others offer nothing like this yet. Revisit if they do.
- **Switching to the backup without asking:** the user chose to be asked, so a paid key never runs up a bill unseen.
- **Thinner upkeep on the plan:** the user chose everything on the plan; the weekly cap is the player's own control.

## Open

- **Asking when the player is away:** the card waits at the PC; whether the question also goes by text (Telegram's
  buttons) when they're away, or waits until they're back (to start: it waits; reminders still go out plainly).
- **How often to try the plan again** (15 minutes to start), and whether a reset time comes with the limit error.
- **The model to suggest first** from the account's list, and whether upkeep should use a lighter one from the same
  list to spend less of the plan.
- **Prompt caching** on plan requests: OpenAI's docs don't say; measure on the user's plan.
- **The Steam build's switch**, once a Steam build exists.

## Order

Each step is usable on its own.

1. **Sign in with ChatGPT, and everything on the plan:** the sign-in (loopback, PKCE, tokens in `KeyStore`, refresh),
   a provider that speaks to the Responses API within its rules, the model list, Settings → Her mind's ChatGPT plan
   with Connected as / Using ChatGPT plan / Manage usage / Disconnect, the limit and signed-out messages (waiting, with
   reminders still coming plainly). A stand-in server for tests.
2. **The backup:** a backup provider on the same page, the ask-first card when the plan runs out, back to the plan by
   herself.
3. **Getting started** offers Continue with ChatGPT first.

## Sources

- [Integrating Sign in with ChatGPT in your open-source app (OpenAI cookbook)](https://developers.openai.com/cookbook/articles/sign-in-with-chatgpt)
- [Sign in with ChatGPT: quickstart (OpenAI)](https://developers.openai.com/siwc/quickstart)
- [Models and inference (OpenAI)](https://developers.openai.com/siwc/token-sharing-open-source/models-and-inference)
- [Preview limitations (OpenAI)](https://developers.openai.com/siwc/token-sharing-open-source/preview-limitations)
- [Sign in with ChatGPT for developers: the OAuth flow, plan usage, and what it means for your API bill (DEV Community)](https://dev.to/hassann/sign-in-with-chatgpt-for-developers-the-oauth-flow-plan-usage-and-what-it-means-for-your-api-bill-48f7)
- [OpenAI expands ChatGPT with apps and third-party sign-in (The Star)](https://www.thestar.com.my/tech/tech-news/2026/10/04/openai-expands-chatgpt-with-apps-and-third-party-sign-in)
- [OpenClaw meets ChatGPT Plus: OpenAI's subscription path (explainx)](https://explainx.ai/blog/openclaw-chatgpt-plus-pro-openai-anthropic-subscription-2026), on Anthropic's change of February 2026
