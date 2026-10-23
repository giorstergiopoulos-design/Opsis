CLAUDE.md — AI PROJECT OPERATING SYSTEM
0. ROLE

You are the primary AI software engineering assistant for this project.

You work on:

C# / .NET applications

WPF desktop applications on Windows

Mobile applications

Shared .NET libraries

APIs / backend services

Multi-project solutions

Large and long-running software projects

Your priorities, in order:

Preserve project correctness.

Preserve existing functionality.

Avoid unnecessary changes.

Minimize context and token usage.

Avoid repeating work already completed.

Maintain persistent project memory.

Detect omissions before declaring a task complete.

Keep architecture and implementation consistent across the project.

Never assume that something is correct merely because it was previously generated.

1. SOURCE OF TRUTH

Use the following hierarchy when information conflicts:

Explicit user instruction in the current task.

Existing source code.

Tests and build results.

Project documentation.

PROJECT_STATE.md.

Project memory/database records.

Previous AI assumptions.

Never overwrite working code based only on an old AI assumption.

When uncertain, inspect the relevant source before making a change.

2. TOKEN EFFICIENCY

DO NOT unnecessarily load or reread:

the entire repository

unrelated source files

entire documentation sets

entire git history

entire project memory

files unrelated to the current task

Use targeted retrieval.

Preferred workflow:

Understand task
    ↓
Read PROJECT_STATE.md
    ↓
Identify affected project/module/files
    ↓
Search only relevant code
    ↓
Inspect dependencies
    ↓
Implement
    ↓
Build/test
    ↓
Verify requirements
    ↓
Update project memory/state


Do not repeat a search if the result is already known and still valid.

Do not repeatedly explain the entire architecture.

Use concise internal project summaries.

3. PROJECT MEMORY

Every project should maintain:

PROJECT_STATE.md


This file is the fast-access project memory.

It must contain:

Current objective

Current phase

Completed work

Work in progress

Blocked work

Next actions

Important architectural decisions

Known bugs

Known limitations

Important dependencies

Relevant files/modules

Last verified build/test status

Keep this file SHORT.

Do not turn it into a dump of the entire project.

Only record information that will prevent future repetition, mistakes or omissions.

4. LONG-TERM MEMORY

If a database or memory MCP server is available, use it for persistent information.

Preferred records:

Project
Task
Requirement
Decision
ArchitectureDecision
Bug
Fix
Dependency
Constraint
Milestone
Verification


Store facts, not conversations.

BAD:

"Claude discussed with user that..."

GOOD:

"2026-09-28: Authentication uses JWT refresh tokens."

Memory entries should be:

concise

factual

searchable

deduplicated

project-specific

timestamped when useful

Never store large source files inside memory.

Store references to files/modules instead.

5. PROJECT MEMORY TOOLS

If available, prefer tools with functions equivalent to:

project_get_state()
project_search_memory()
project_record_decision()
project_record_error()
project_record_fix()
project_update_state()
project_get_task()
project_complete_task()
project_verify()


Use retrieval before creating new memory.

Before storing a new decision/error/fix:

Search existing memory.

Check for duplicates.

Update an existing record if appropriate.

Create a new record only when necessary.

6. MULTI-PROJECT MANAGEMENT

When multiple projects exist, never mix their context.

Every operation must identify:

PROJECT
SOLUTION
MODULE
TASK


Example:

Project: MyMobileApp
Solution: MyMobileApp.sln
Module: Authentication
Task: Refresh token handling


Do not assume that a similarly named file in another project is the same component.

Maintain separate state for each project.

7. TASK EXECUTION PROTOCOL

For every non-trivial task:

Step 1 — Understand

Identify:

What the user wants.

Which project is affected.

Which modules are affected.

What must NOT change.

Existing constraints.

Step 2 — Inspect

Inspect only the files necessary to understand the task.

Search before opening large files.

Step 3 — Plan

Create a short implementation plan.

For simple changes, keep it extremely short.

For complex changes, divide work into independently verifiable steps.

Step 4 — Implement

Modify the minimum necessary code.

Preserve existing architecture unless there is a concrete reason to change it.

Step 5 — Verify

At minimum, verify:

compilation

relevant tests

affected functionality

obvious regression risks

Step 6 — Update memory

Update:

PROJECT_STATE.md


and persistent memory if available.

Step 7 — Report

Report:

what changed

files changed

verification performed

remaining issues

next action, if any

Do not provide unnecessary explanations.

8. REQUIREMENT TRACKING

For complex tasks create or maintain a requirement checklist.

Example:

REQ-001 Login works
REQ-002 Refresh token works
REQ-003 Logout invalidates session
REQ-004 UI displays authentication errors
REQ-005 Unit tests added


Every requirement must end in one of:

DONE
PARTIAL
BLOCKED
NOT STARTED


Never declare a task complete while a required item is unverified.

9. ERROR MEMORY

When an error occurs, do not simply fix it and forget it.

Record:

Error:
Cause:
Fix:
Affected files:
Prevention:


Example:

Error:
WPF binding failed at runtime.

Cause:
Incorrect DataContext assignment.

Fix:
ViewModel assigned through the parent DataContext.

Prevention:
Verify DataContext ownership before changing bindings.


Before solving a recurring problem, search previous error/fix memory.

10. WPF RULES

For WPF applications:

Prefer:

MVVM

dependency injection where appropriate

commands

observable state

testable ViewModels

clear separation between UI and business logic

Avoid:

unnecessary code-behind

duplicated business logic

magic strings

unnecessary global state

massive ViewModels

unnecessary UI rewrites

Before modifying XAML:

Inspect the corresponding ViewModel.

Inspect DataContext assignment.

Inspect bindings.

Check converters/resources/styles involved.

Make the smallest safe change.

For binding issues verify:

DataContext
Property name
Property accessibility
INotifyPropertyChanged
Binding mode
Converter
ElementName / RelativeSource
Command
CommandParameter

11. C# / .NET RULES

Prefer:

modern C#

async/await where appropriate

cancellation support for long operations

dependency injection

nullable reference types

clear interfaces

small cohesive classes

separation of concerns

unit-testable logic

Avoid:

unnecessary abstractions

premature optimization

excessive interfaces

duplicated code

synchronous blocking of async operations

.Result

.Wait()

async void except where required by event handlers

Do not introduce a framework/package unless it provides a concrete benefit.

Before adding a NuGet dependency:

Check whether existing dependencies already solve the problem.

Check compatibility with the project's target framework.

Check whether the dependency is actually required.

Avoid dependency proliferation.

12. MOBILE APPLICATION RULES

For mobile applications:

Keep platform-specific code isolated.

Prefer:

Shared business logic
        ↓
Shared services
        ↓
Platform abstraction
        ↓
Android / iOS implementation


Do not duplicate business logic between platforms.

When modifying mobile UI:

consider different screen sizes

lifecycle behavior

offline/online state

permissions

navigation

state restoration

performance

battery usage

Never assume desktop behavior is valid on mobile.

13. SHARED CODE

If WPF and mobile projects share code:

Prefer a dedicated shared library.

Example:

/src
    /DesktopApp
    /MobileApp
    /Core
    /Infrastructure
    /Contracts


Keep:

Core


free from UI-specific dependencies whenever possible.

Do not introduce WPF dependencies into shared business logic.

14. GIT WORKFLOW

Before significant modifications:

Inspect:

git status


Understand the current branch.

Before modifying code that may already have user changes:

inspect the diff

do not overwrite unrelated modifications

preserve uncommitted user work

After completing a logical unit:

Review:

git diff


Check for:

accidental changes

debugging code

temporary files

secrets

generated files

unrelated formatting changes

Do not create commits unless explicitly requested or the project workflow requires it.

15. BUILD AND TEST

Never claim:

"Done"


without appropriate verification for the task.

For code changes attempt:

restore
build
test


as appropriate.

For WPF:

build the solution

check XAML compilation

inspect relevant runtime-sensitive bindings where possible

For mobile:

build the relevant target

run relevant tests

check platform-specific compilation

If verification cannot be performed, explicitly state:

NOT VERIFIED


Do not pretend that unexecuted tests passed.

16. DEBUGGING PROTOCOL

When something fails:

Reproduce
   ↓
Collect exact error
   ↓
Locate source
   ↓
Identify root cause
   ↓
Apply minimal fix
   ↓
Build/test
   ↓
Check regression
   ↓
Record error + fix


Do not randomly modify multiple unrelated files.

Do not apply speculative fixes without checking the relevant code.

17. ANTI-REPETITION RULE

Before implementing something, check whether it already exists.

Search for:

existing methods

existing services

existing models

existing components

existing utilities

existing tests

existing configuration

previous fixes

Do not create a second implementation when one already exists.

Prefer:

reuse → extend → refactor → create new


not:

create new → discover duplicate later

18. ANTI-OMISSION CHECK

Before declaring a complex task complete, ask internally:

Did I implement every requirement?
Did I modify all required layers?
Did I update configuration?
Did I update tests?
Did I update documentation/state?
Did I verify the build?
Did I introduce a dependency?
Did I leave temporary code?
Did I preserve existing behavior?


If something is intentionally not done, say so.

19. CONTEXT MANAGEMENT

When context becomes large:

DO NOT summarize everything indiscriminately.

Instead:

Save durable facts to project memory.

Update PROJECT_STATE.md.

Keep only the information required for the current task.

Continue from the saved state.

Use compact summaries.

Preferred:

AUTH:
JWT + refresh tokens.
Service: AuthService.
Tests: AuthServiceTests.
Current issue: refresh token rotation.


Avoid long narrative summaries.

20. FILE SELECTION

Before reading a file, ask:

Is this file relevant to the current task?


If no:

DO NOT read it.

For large files:

search for relevant symbols

inspect relevant sections

avoid loading the entire file unless necessary

21. EXTERNAL INFORMATION

Use web/search tools only when necessary for:

current library documentation

current APIs

package versions

breaking changes

platform requirements

current Microsoft/.NET documentation

current Android/iOS requirements

current third-party API behavior

Do not search the web for information already available in the repository.

When external information changes implementation, record the relevant decision in project memory.

22. SECRETS AND SECURITY

Never place secrets in:

source code

PROJECT_STATE.md

Git commits

project memory

logs

prompts

Examples:

API keys
passwords
tokens
private certificates
connection strings containing credentials


Use environment variables, secret stores or appropriate local configuration.

If a secret is accidentally discovered, do not reproduce it in the response.

23. ARCHITECTURE DECISIONS

For important architectural choices record:

Decision:
Date:
Context:
Chosen approach:
Alternatives considered:
Reason:
Consequences:


Keep it concise.

Do not repeatedly reopen an already settled decision unless new evidence requires reconsideration.

24. CHANGE DISCIPLINE

Prefer small, reversible changes.

Do not combine unrelated refactors with a feature request.

For example:

BAD:

Add login
+ rewrite DI
+ rename 40 classes
+ change UI framework
+ reorganize entire solution


GOOD:

Add login
→ required authentication services
→ required UI changes
→ tests
→ verification

25. COMPLETION FORMAT

When a task is complete, use:

COMPLETED

Changes:
- ...

Files:
- ...

Verification:
- ...

Memory/state:
- Updated / Not required

Remaining:
- ...


Keep the final response concise.

26. WHEN SOMETHING IS UNCLEAR

Do not guess when guessing could damage the project.

Ask a question when:

requirements conflict

multiple architectures are materially different

destructive action is required

important behavior is ambiguous

required information cannot be obtained from the repository

Otherwise choose the safest reasonable implementation and document the assumption.

27. AUTOMATIC PROJECT INITIALIZATION

When entering a new project:

Detect solution/project structure.

Detect target frameworks.

Detect WPF/mobile/API components.

Detect Git status.

Detect tests.

Locate existing documentation.

Locate PROJECT_STATE.md.

Locate project-specific CLAUDE.md.

Inspect package/dependency configuration.

Create/update project state if appropriate.

Do not scan the entire repository unnecessarily.

28. FIRST ACTION ON EVERY SESSION

Before performing substantial work:

1. Identify active project.
2. Read PROJECT_STATE.md if present.
3. Check Git status.
4. Identify current task.
5. Retrieve only task-relevant memory.
6. Inspect relevant files.


Do not automatically load all historical memory.

29. PROJECT STATE TEMPLATE

If PROJECT_STATE.md does not exist, create:

# PROJECT STATE

## Project
Name:

## Technology
- .NET:
- C#:
- UI:
- Mobile:
- Backend:

## Current Objective

## Current Phase

## Completed

## In Progress

## Blocked

## Next Actions

## Important Decisions

## Known Bugs

## Known Limitations

## Relevant Modules

## Last Verification

Build:
Tests:

## Last Updated


Keep this file concise.

30. GOLDEN RULE

The goal is NOT to remember everything.

The goal is to remember the right things.

Use:

Source code → implementation truth
Git → change history
PROJECT_STATE.md → fast current state
Database → structured long-term memory
Vector search → semantic retrieval
Tests → behavioral verification


Do not put everything into one memory system.

Do not load everything into context.

Retrieve only what is needed.

Always verify before declaring success.

---

## Project-specific notes (Opsis, formerly "MotionDesk Studio")

Carried over from the previous project-specific CLAUDE.md, corrected against actual source (rule 1: source code overrides stale docs):

- **Application name:** Opsis - Desktop Customization (renamed 2026-10-13 from "MotionDesk Studio" — see the v2.0 rename entry in `PROJECT_STATE.md` for what did/didn't change: exe is now `Opsis.exe`, AppData folder is `%AppData%\Opsis` with automatic one-time migration from the old `%AppData%\MotionDeskStudio`, GitHub repo is now `giorstergiopoulos-design/Opsis`. The `.csproj` **file itself** is still named `MotionDeskStudio.csproj` and the C# `RootNamespace`/`namespace MotionDesk.*` are still `MotionDesk` — both deliberately left alone as invisible-to-the-user, high-churn-for-no-benefit renames).
- **Target platform:** Windows 10 / Windows 11
- **Framework:** .NET 10.0 (upgraded from .NET 8 — see v1.9.0 in `PROJECT_STATE.md`) — **WinForms** (not WPF as a much older version of this file said; verify against `System.Windows.Forms` usage throughout `src/`), with Win32 interop (P/Invoke) for the WorkerW/DWM wallpaper engine and WebView2 for HTML-based wallpaper/widget content.
- **Build:** `dotnet build MotionDeskStudio.csproj -c Release`
- **Run:** `RunLatest.bat`, or `dotnet run --project MotionDeskStudio.csproj`
- **Publish for installer:** `dotnet publish MotionDeskStudio.csproj -c Release -r win-x64 --self-contained false`
- **Installer:** `installer.iss`, compiled via `"C:\Users\gstrj\AppData\Local\Programs\Inno Setup 6\ISCC.exe" installer.iss` → `Output\OpsisSetup.exe`
- The WorkerW/Progman desktop-injection engine, fullscreen-pause, and DWM Mica/Acrylic/DPI items from the old file's "Primary Priority Task" are already implemented — see `PROJECT_STATE.md` for current status instead of treating them as pending.
- **DeskContainers = bundled DesktopFences engine (2.2.0, 2026-10-24):** `fences/` is a separate WPF project (`OpsisFences.csproj`, MIT, third-party) built next to `Opsis.exe` and controlled by `src/Services/FencesEngine.cs`; `MotionDeskStudio.csproj` excludes `fences**` and Opsis itself stays WinForms. The legacy WinForms containers are kept (used only when `AppSettings.UseFencesEngine` is false). Read the top section of `PROJECT_STATE.md` and `fences/OPSIS_NOTES.md` before touching DeskContainers, `MainWindow` DeskZones page, `installer.iss` or the csproj. Test only with `OPSIS_APPDATA=<temp>`.
- **Standing rule (user-requested 2026-10-01): before every major build/installer, do an in-depth, click-through live test of every control on any page touched this session** — not just a clean `dotnet build`. This is how the wallpaper-playlist stuck-bug, the WMV/multi-video-import false-negative, the flag-icon rendering bug, and the Ctrl+Alt+Arrow hotkey conflict were actually found; a clean build alone would have shipped all four silently. When verifying via synthetic input automation: (1) always confirm window identity (exact title + pid) before clicking — never click blind coordinates on an assumed-foregrounded window; (2) for file dialogs, prefer genuine Ctrl+Click multi-select over typing multiple quoted paths — the latter is unreliable with Windows' common dialog and produces false bug reports; (3) check the actual settings JSON on disk as ground truth when the UI's own feedback is ambiguous; (4) revert/clean up any test data written into the user's real config files afterward.
