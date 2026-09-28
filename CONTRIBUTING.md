# Contributing to OpenGG

OpenGG aims to play Gearhead Garage as the original does, with the player's own copy of it. The rules below follow
from that.

## Where things go

- **Bugs**, and places where OpenGG **differs from the original**: [Issues](../../issues/new/choose), one problem per
  issue. Search first.
- **Questions and ideas**: [Discussions](../../discussions).
- **Security problems**: privately, see [SECURITY.md](SECURITY.md).

## Reporting a bug

Give the OpenGG version (on the sign-in sheet's BETA stamp, or in the window's title), whether you play with the
original's content or the placeholders and in which look, what you did, what you expected and what happened. Attach the
log (`%APPDATA%\Godot\app_userdata\OpenGG\logs\godot.log`) and, if it helps, a screenshot or your save. **Never attach
files of the original game** (its `Data` folder, `.car`, `.dat` or `.mek` files and the like).

## Reporting a difference from the original

Say what the original does and how you know it: a video or a screenshot of the running original is the best evidence.
Say which release you played (1999 or 2002). [docs/FIDELITY.md](docs/FIDELITY.md) lists what was measured and what
differs on purpose.

## Pull requests

- Discuss anything big in an issue or a discussion first, and keep each pull request to one change.
- Rebase on `main`. The CI must pass; locally: `npm ci`, `npm run typecheck`, `npm run gen:assets`, then
  `dotnet test core/OpenGG.Core.Tests`.
- Write code like the code around it: naming, comments, idioms.
- A change of behaviour updates [docs/FIDELITY.md](docs/FIDELITY.md) (one bullet: its tag, the rule, briefly how it
  was found) and proposes a line for [CHANGELOG.md](CHANGELOG.md).

### The clean-room rules

OpenGG stays free of the original's code and content:

- No decompiling or disassembling the original's programs, and nothing taken from anyone who did. Behaviour comes from
  measuring the running game, file formats from its data files.
- Nothing of the original goes into the repository: no files, extracted assets, texts or screen captures. OpenGG reads
  them from the player's copy at runtime.
- Assets: generated ones carry the `ai_` prefix; pictures, sounds or models made by others only under CC0 (or a licence
  that fits GPL-3.0), each listed with its source.

### AI tools

Welcome as tools (OpenGG itself is written with one). You must understand, and be able to explain, what you submit, and
say in the pull request that you used one.

## Licence

Contributions are made under the [GPL-3.0](LICENSE), like the rest of OpenGG; assets under their stated licences. Your
name and email stay in the git history (use GitHub's noreply address if you prefer).

Everyone taking part follows the [Code of Conduct](CODE_OF_CONDUCT.md).
