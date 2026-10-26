# Blazor design system

Agentic Lab's Blazor design system needs no JavaScript framework runtime. Shared controls
have no dependency on Flow state, AiService or npm.

## Ownership

- [design-system.css](../src/AgenticLab.Web/wwwroot/design-system.css): document-level `--lab-*` tokens
  and local fonts, loaded once by [App.razor](../src/AgenticLab.Web/Components/App.razor).
- [app.css](../src/AgenticLab.Web/wwwroot/app.css): application resets and framework styles.
- [Extensibility controls](../src/AgenticLab.Extensibility/Components): `LabButton`, `LabField`,
  `LabStatus`, `MiniIcon`, reusable by Web and examples. [Web shared controls](../src/AgenticLab.Web/Components/Shared)
  own page/dock presentation. Each component owns its scoped CSS.
- `/design-system`: interactive catalogue, with no backend calls. Available only in Development;
  returns 404 otherwise and is absent from production navigation.

Host branding belongs to [example modules](examples.md), using local RCL SVG masks with a neutral
fallback. It must not change shared tokens, contributor meanings, layout or run state.

## Tokens

| Family | Purpose |
| --- | --- |
| `--lab-font-sans`, `--lab-font-mono` | IBM Plex Sans for UI; IBM Plex Mono for metadata and code |
| `--lab-text-*`, `--lab-leading` | Fixed type scale and readable line height; no viewport-sized text |
| `--lab-surface*`, `--lab-page` | White and quiet neutral backgrounds |
| `--lab-ink`, `--lab-muted`, `--lab-subtle` | Text hierarchy; subtle is for nonessential decoration |
| `--lab-accent*`, `--lab-on-accent` | Green commands, focus and selected states |
| `--lab-user`, `--lab-host`, `--lab-model`, `--lab-tool` | Actor markers, not small body text |
| `--lab-danger*`, `--lab-warning*`, `--lab-info*` | Semantic states, always accompanied by text |
| `--lab-contributor-*` | Fixed context-provenance colours, independent of actor/vendor branding |
| `--lab-space-1` through `--lab-space-8` | 4, 8, 12, 16, 20, 24, 32 and 40 pixel spacing |
| `--lab-control-*`, `--lab-icon-size` | Stable control geometry |
| `--lab-border*`, `--lab-radius-*` | Fine dividers and 4/6/8px corners |
| `--lab-grid-image`, `--lab-grid-size` | Shared subtle dotted diagram surface |
| `--lab-focus-*`, `--lab-duration`, `--lab-easing` | Visible keyboard focus and restrained motion |
| `--lab-shadow-overlay`, `--lab-backdrop`, `--lab-layer-*` | Overlays, not decorative section cards |

Reuse tokens, not palette literals. Feature aliases must not self-reference or mix foreground and
background roles. Contributor provenance, risk levels and signed chart scales keep distinct meanings.

## Components

`AppHeader` provides the product mark, page context and repository link; children supply page actions.
Keep it inside the page's render boundary, without navigation/run state. Agent guide links open a
new tab from Flow/Discovery; Learn has no Discovery entry.

`LabButton` supports `primary`, `secondary`, `quiet`, `danger`, icons, disabled/busy and pressed states.
`Label` is required even for icon-only commands. Use `OnClick` for actions and anchors for navigation.
Keep labels and icon slots stable during progress.

```razor
<LabButton Label="Send" Icon="send" Variant="primary" OnClick="SendAsync" />
<LabButton Label="Pause" Icon="pause" IconOnly="true" OnClick="PauseAsync" />
```

`LabField.For` labels a caller-owned native input; binding/validation stay with the caller.
For `Hint` or `Error`, set `aria-describedby="<id>-description"` and `aria-invalid` on errors.
`Inline` supports compact selection bars.

```razor
<LabField For="workspace" Label="Workspace" Error="@Error">
  <input id="workspace" @bind="Workspace" aria-invalid="@(Error is not null ? "true" : "false")"
           aria-describedby="workspace-description" />
</LabField>
```

`LabSegmented` groups mutually exclusive buttons with caller-supplied `aria-pressed` and callbacks.
Render ARIA booleans as strings `"true"`/`"false"`, not minimized Razor boolean attributes. It is not
a tablist: real tabs need tab/tabpanel relationships, arrow navigation and a managed tab stop.

`LabStatus` pairs text with a `neutral`, `success`, `warning` or `danger` marker. `Busy` animates only
the marker; the label must communicate state without colour or motion.

`SidePanel` resizes/collapses docks. `ShowHeader` defaults to true; tabbed content can supply its own
header. Splitters are focusable separators supporting arrows, Shift+arrows, Home/End and dragging;
the owning layout controls geometry. Use `MiniIcon` and the pinned Lucide assets for icons.
`KeepContentMounted` is opt-in: after first expansion, collapsing hides content and splitters with
native `hidden` attributes instead of disposing them. The owner controls deferred content updates.

`PageNotice` shares header/typography without changing error/404 status or diagnostics. The reconnect
dialog shares tokens without replacing its framework lifecycle. Native modals must retain focus
containment, Escape dismissal and focus restoration.

## Accessibility and layout

Use native controls: labelled icons for tools, checkboxes for binary options, segmented mode choices,
and sliders/inputs for numbers. Actions must work without hover. Preserve focus, disabled/error states
and WCAG AA text contrast. Actor colours are supplemental; reduced motion changes decoration, never
backend pacing or progression.

Use flush sections, not nested cards; fixed type sizes, zero letter spacing and container-driven reflow.
Set `min-width: 0` on flex/grid children and wrap labels. Only payload areas may scroll horizontally.
Reflow must not mutate preferences or run state. Features own grids/diagrams, using shared spacing.

## Assets and contributions

Local Latin font subsets use `@fontsource/ibm-plex-sans` and `@fontsource/ibm-plex-mono` **5.3.0**:
Sans 400/500/600 and Mono 400. [Fonts](../src/AgenticLab.Web/wwwroot/fonts) retain their
[OFL notices](../src/AgenticLab.Web/wwwroot/licenses). Pinned Lucide/Octicons assets retain their
notices too. No runtime CDN is used.

Before adding a primitive, find two real consumers or a repeated accessibility contract. Use parameters
and events, not feature state; add XML parameter summaries and a catalogue example. Build Web, inspect
desktop/mobile consumers and run Web tests for interaction/state changes. The
[browser smoke guide](../tools/README.md) checks the catalogue and real pages without model calls.