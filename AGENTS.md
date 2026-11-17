# ObFramework AI instructions

Before repository work, read the canonical [agent instructions](ai/AGENTS.md). They define architecture conventions, documentation ownership and the required CLI reference workflow for System changes.

Documentation/skill paths in that file are relative to ai/; framework source paths are relative to this checkout. The discoverable [documentation skill](.agents/skills/obframework-docs/SKILL.md) forwards to the submodule.

If ai/ is missing or empty, initialize it from the framework repository root:

```powershell
git submodule update --init --recursive ai
```
