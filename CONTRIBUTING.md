# Contributing

Thank you for wanting to help with Beside the Cabinet. Bug reports, ideas and pull requests are all welcome.

## Before you start

- **A bug:** open an issue with what you did, what you expected and what happened. The app's log helps:
  `%APPDATA%\Godot\app_userdata\Beside the Cabinet\logs\godot.log` (leave out anything private, and never paste an API
  key).
- **Something bigger:** open an issue first, so we can agree on it before you spend the time.
- **Security problems:** don't open a public issue; use GitHub's private vulnerability reporting on this repository.

## The contributor agreement

Every pull request needs a signed contributor agreement ([CLA.md](CLA.md)). The bot asks for it on your first pull
request, and signing takes one click with your GitHub account.

Why: the code is licensed to everyone under the PolyForm Noncommercial License, and the copyright holder also publishes
the app themselves, possibly as a paid build. The agreement lets your contribution go into every build of the app. You
keep the copyright to your work.

## Building and testing

The manual in the [README](README.md) has the details. In short:

- `dotnet test InsertCoin.slnx` runs the tests (Core, Adapters, Mind).
- `dotnet build src/InsertCoin.Game/InsertCoin.Game.csproj` builds the game; set `GODOT` to your Godot 4.7.2 .NET
  editor, then `run.cmd` runs it, and `demo.cmd` runs it on a profile of its own.
- `tools/dev/shoot.ps1` and `tools/dev/clip.ps1` run it in a throwaway profile, for screenshots and clips: please show
  what a change looks like in your pull request.

## How the code reads

- Logic goes in `src/InsertCoin.Mind` (no Godot), with tests; the Godot app in `src/InsertCoin.Game` stays thin.
- Plain words for names; doc comments say what something means for the player; British spelling (colour, summarise).
- Test names are sentences: `A_reminder_goes_off_ahead_of_its_item`.
- Behaviour is fixed in code before prompts: a rule in the code that checks the model's answer beats one more line of
  instructions.
- Builds stay at zero warnings.
- A commit message starts with a plain sentence saying what changed for the player.

## What never goes in

- API keys, tokens, private links, or anything personal.
- Characters, voices, models or art you don't have the right to share, such as game rips. Voices and models from
  games and anime are fine to use yourself, never to commit.
- Large models: `tools/models/fetch.py` fetches them.
