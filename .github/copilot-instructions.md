---
description: AI rules derived by SpecStory from the project AI interaction history
globs: *
---

## Headers

## TECH STACK

## PROJECT DOCUMENTATION & CONTEXT SYSTEM

## CODING STANDARDS

## WORKFLOW & RELEASE RULES

## DEBUGGING

## AI AGENT INSTRUCTIONS

- Analyze the codebase to generate or update `.github/copilot-instructions.md` for guiding AI coding agents.
- Focus on discovering the essential knowledge that would help an AI agents be immediately productive in this codebase, including:
    - The "big picture" architecture that requires reading multiple files to understand - major components, service boundaries, data flows, and the "why" behind structural decisions
    - Critical developer workflows (builds, tests, debugging) especially commands that aren't obvious from file inspection alone
    - Project-specific conventions and patterns that differ from common practices
    - Integration points, external dependencies, and cross-component communication patterns
- Source existing AI conventions from `**/{.github/copilot-instructions.md,AGENT.md,AGENTS.md,CLAUDE.md,.cursorrules,.windsurfrules,.clinerules,.cursor/rules/**,.windsurf/rules/**,.clinerules/**,README.md}` (do one glob search).
- If `.github/copilot-instructions.md` exists, merge intelligently - preserve valuable content while updating outdated sections
- Write concise, actionable instructions (~20-50 lines) using markdown structure
- Include specific examples from the codebase when describing patterns
- Avoid generic advice ("write tests", "handle errors") - focus on THIS project's specific approaches
- Document only discoverable patterns, not aspirational practices
- Reference key files/directories that exemplify important patterns
- Update `.github/copilot-instructions.md` for the user, then ask for feedback on any unclear or incomplete sections to iterate.
- When a user provides compiler errors indicating missing parse functions within `DataTableExtension`, the agent should proactively search for the `DataTableExtension` class file and implement the necessary `Parse...` functions to resolve the errors. The agent should then present the proposed solution to the user for review and approval before committing changes.
- When implementing missing methods, such as `Parse...` functions in `DataTableExtension`, use the plan tool to outline the implementation steps. Use `grep` to efficiently search for the `DataTableExtension` file and related processors to understand the specifics.
- If the user reports errors related to missing `Read...` definitions in `BinaryReader`, the agent should implement the corresponding extension methods for `BinaryReader` to resolve these errors. When implementing these extensions:
    - Place the new read methods like `ReadFix64` right after existing parse functions in `DataTableExtension` for clarity.
    - Ensure array parsing handles edge cases properly by returning null for non-positive lengths and mirroring the writer behavior, especially for string reads.
    - When implementing `BinaryReader` extensions, confirm or create a way to read 7-bit encoded integers since `BinaryReader`'s built-in method is protected. Search the codebase for any existing `Read7BitEncodedInt32` extensions before proceeding.
- When implementing `ParseArray<T>`, ensure it properly handles enums by detecting if T is an enum type and then parsing each element with ParseEnum, especially when strings like "ItemTag.AAA" include prefixes. Extract the actual enum value portion after the dot and call ParseEnum for each item, considering flags and logging errors if parsing fails.

## EXAMPLES & REFERENCES