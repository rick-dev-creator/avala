# Keyboard shortcuts

Every shortcut in Avala, with ⌘ on macOS and Ctrl on Windows and Linux.

## What it does

Avala presses ⌘ on macOS where Windows and Linux press Ctrl, and its tooltips show the key for your system: ⌃ ⌥ ⌘ ⇧, Return and Delete on macOS; Ctrl+, Alt+, Shift+, Enter and Backspace elsewhere. Below, **Mod** means ⌘ on macOS and Ctrl elsewhere.

## How to use it

### Anywhere

| Keys | Does |
| --- | --- |
| Mod+N | New job |
| Mod+D | Pending decisions |
| Mod+R | Open the review |
| Mod+I | Show or hide the inspector |
| Mod+\ | Show or hide the sidebar |
| Mod+Shift+F | Focus mode; Esc or the same keys leave it |
| F11, or Ctrl+⌘+F on macOS | Full screen |
| Mod+C, Mod+A | Copy, Select All in a conversation |
| Mod+F | Find in the conversation; Enter and Shift+Enter step through the matches, Esc closes it |

### Writing

| Keys | Does |
| --- | --- |
| Enter | Send, or start from the welcome |
| Shift+Enter | Add a line |
| Mod+Enter | Send, when Send with Mod+Enter is on; start a New job |
| Mod+Shift+Enter | Start a New job without a repository |
| Esc | Stop the agent; the session stays open |
| ↑, ↓ | Choose a suggestion while the `@` mentions are open |
| Enter, Tab | Write the chosen mention |
| Esc | Close the mentions, before it stops the agent |

### Answering

| Keys | Does |
| --- | --- |
| Mod+Enter | Allow a permission, or Answer a form |
| Mod+Backspace | Deny a permission, or Decline a form |
| In Decisions: Up/Down or J/K | Move between decisions |
| In Decisions: 1–9 | Choose an option |
| In Decisions: Enter, Shift+Enter, Backspace | Answer, write a note, deny |

### Reviewing and dialogs

| Keys | Does |
| --- | --- |
| Mod+Enter | Send back, in the send-back box |
| Esc | Cancel a discard, or close the review, a canvas, a file preview or Decisions |
| Enter, Esc | Confirm or cancel Clone and New repository; Continue or Skip in the welcome tour |

## Settings and files

**Send with Mod+Enter**, in Settings, Appearance: when off, Enter sends and Shift+Enter adds a line. It is kept in `appearance.json` as `"send": "commandEnter"` or `"enter"`.

## Good to know

Shortcuts cannot be rebound yet.

## See also

- [Jobs and conversations](jobs-and-conversations.md)
- [Permissions and autonomy](permissions-and-autonomy.md)
