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
- When designing interactable objects:
    - The core principle is that interactable objects are responsible for defining *what* they can do, while the player/system determines *when* and *how* the interaction occurs. This promotes decoupling and extensibility.
    - Use the following abstractions:
        - **IInteractable**: Defines the interactable object's capabilities, including available interaction options, whether interaction is currently possible, and an entry point for initiating interaction.
        - **InteractionOption**: Represents a specific interaction action (e.g., "Talk (E)", "Chop (Hold F)", "Gather (Timed QTE)"). Contains display text, key binding, input mode, priority, and an execution callback/command.
        - **InteractionDetector**: A component on the player that detects nearby interactable objects (e.g., using a trigger, raycast, or field of view).
        - **InteractionResolver**: Selects the single interactable object to display prompts for from a collection of candidates.
        - **InteractionUI**: Responsible for displaying the interaction options for the currently selected object.
        - **InputInterpreter**: Translates player input (e.g., key press, hold, timed input) into a trigger for a specific interaction option.
    - Strive to design the system so that adding new interactable objects, input modes, selection logic, or UI styles requires modifying as few layers as possible.
    - Consider these architectural approaches:
        - **方案 A: 组件接口 + 中央 InteractionManager**: Suitable for most Unity projects.
            - Each interactable object has an `Interactable` component (implementing `IInteractable`) and optional `InteractionProvider` components (offering different interaction options).
            - The player has an `InteractionDetector` and an `InteractionManager` (which selects the object, drives the UI, listens for input, and triggers interaction).
            - Use a scoring system in the resolver to determine the best candidate based on factors like distance, viewing angle, screen position, and occlusion. Implement hysteresis to avoid UI flickering.
            - Implement different `IInteractionInputMode` implementations (e.g., `PressMode`, `HoldMode`, `TimingMode`) to handle different input patterns.
        - **方案 B: 事件/消息总线 (GameEvent) + 交互命令 (Command)**: Best if the project already uses events extensively.
            - The `InteractionManager` only selects the target, displays options, and emits an `InteractionRequested` event.
            - Subscribers (e.g., `DialogueSystem`, `CraftingSystem`, `GatherSystem`) handle the actual execution.
        - **方案 C: 数据驱动 (ScriptableObject/配置表) + 通用 Interactable**: Suitable for projects with many interactable objects and frequent configuration changes.
            - The `Interactable` component only stores a reference to an `InteractableConfig` (ScriptableObject or table ID).
            - The configuration defines the available options, input modes, and execution commands.
            - An `InteractionExecutor` dispatches the commands based on their type.
    - When combining approaches, start with the structure of **方案 A** and consider incorporating aspects of **B** and **C** as needed for modularity and data-driven configuration.
    - Key Interfaces:
        - `IInteractable.GetOptions(actor)`: Returns a `List<InteractionOption>`.
        - `InteractionOption`: Contains properties like `displayName`, `InputActionReference action` (or key enum), `IInteractionInputMode mode`, `int priority`, `Func<bool> canExecute`, and `Action execute` (or `CommandId + params`).
        - `IInteractionResolver.Resolve(candidates, actor)`: Returns the "current target."
        - `IInteractionInputMode.Update(option, inputState)`: Outputs `Triggered/Progress`.
    - **For projects using a mature `GameEventArgs` event system and the new InputSystem, the preferred approach for interactable objects is 方案 B: 事件/消息总线 (GameEvent) + 交互命令 (Command).** In this approach:
        - The `InteractionManager` selects the target, displays options, and emits an `InteractionRequested(optionId, targetId)` event.
        - Subscribers (e.g., `DialogueSystem`, `CraftingSystem`, `GatherSystem`) handle the actual execution.
        - Interactive objects provide information about their identity, available options, and necessary context (e.g., resource ID), and do not directly execute UI or animation logic.

## EXAMPLES & REFERENCES