# RapidRelief — Frontend UI/UX Design System & Engineering Guide

> **MANDATORY FOR ALL DEVELOPERS AND AI CODING AGENTS**  
> Every engineer and AI agent (Copilot, Claude, Antigravity, etc.) **must read this document before creating or modifying any frontend page, component, or stylesheet**.  
> This guide defines the design tokens, component standards, accessibility rules, UX heuristics, and implementation patterns for **RapidRelief**.

---

## 1. Design Philosophy & Identity

RapidRelief is an **AI-Smart Disaster Response & Emergency Management System**. The UI must feel:
- **Trustworthy & Authoritative:** Government- and healthcare-grade clarity.
- **Fast & Operational:** Low cognitive load; information is scannable in seconds under high-stress conditions.
- **Accessible & Ethical:** WCAG 2.1 AA compliant, visible focus states, high color contrast, keyboard-first navigation, and zero motion sickness (`prefers-reduced-motion`).
- **Resilient:** Beautiful across Light and Dark themes, functional offline, and gracefully degraded when backend or AI services are unavailable.

### 🚫 Design Anti-Patterns (Strictly Forbidden)
1. **Never use generic or harsh hex colors.** Always use semantic `--rr-*` CSS tokens.
2. **Never use red for ordinary actions.** `--rr-primary` (Forest Green `#1e7a5a`) is for primary actions. `--rr-danger` (Rescue Red `#e53935`) is **strictly reserved** for emergencies, critical alerts, SOS, and destructive actions. **Blue is not part of the palette** (D-074) — a blue hex anywhere in the client is a regression.
3. **Never use emojis as icons.** Always use `<AppIcon Name="..." />`.
4. **Never render raw HTML (`MarkupString`, `innerHTML`, `outerHTML`, or markdown renderers).** All content must use Blazor's `@-interpolation` to prevent XSS (enforced by `ClientRenderSafetyTests`).
5. **No decorative glassmorphism or 3D clutter.** The header is the only translucent surface (`--rr-backdrop`). Everything else uses clean, solid surface tokens.

---

## 2. Design Token System

All components must strictly use tokens defined on `:root` and `[data-theme='dark']` in `src/RapidRelief.Client/wwwroot/css/app.css`. Hex values below are the **current** brand palette (D-074, 2026-09-03) — see [RapidRelief-Website-Theme.md](RapidRelief-Website-Theme.md) for the brand rationale and [design.md](design.md) for the shorter mandatory rulebook.

### 🎨 Semantic Colors & Surfaces

```css
/* Surface Tokens — Warm Ivory / Sage Mist family, green-tinted dark (never navy) */
--rr-bg                 /* Main background (Light: #faf7f2 | Dark: #111513) */
--rr-surface            /* Card / Container surface (Light: #ffffff | Dark: #181d1a) */
--rr-surface-2          /* Elevated section / muted background (Light: #eef3ee | Dark: #1f2722) */
--rr-surface-3          /* Hover / active surface (Light: #e0e8e1 | Dark: #2a342e) */

/* Text & Foreground */
--rr-text               /* High-contrast primary text (Light: #2f3431 | Dark: #f3f5f4) */
--rr-text-2             /* Secondary body text (Light: #4d5751 | Dark: #aeb9b2) */
--rr-text-3             /* Muted captions & metadata (Light: #6b7280 | Dark: #8b968f) */

/* Borders */
--rr-border             /* Standard subtle border (Light: #d9e0db | Dark: #2c3631) */
--rr-border-strong      /* High-contrast card & input border */

/* Actions & Brand — Forest Green */
--rr-primary            /* Primary button & brand action (Light: #1e7a5a | Dark: #2cb67d) */
--rr-primary-hover      /* Hover state for primary action (Light: #176448) */
--rr-on-primary         /* Text color on primary button (Light: #ffffff | Dark: #0b241a) */
--rr-link               /* Hyperlink color (Light: #1e7a5a | Dark: #7fd7b2) */
--rr-brand              /* Logo / brand mark */

/* Severity & Semantic Soft Pairs */
--rr-danger             /* Critical alarm, emergency, destructive (Light: #e53935 | Dark: #ef5350) */
--rr-danger-soft        /* Soft emergency banner background (#ffebee) */
--rr-danger-soft-text   /* Soft emergency text (#c62828) */
--rr-danger-soft-border /* Soft emergency border */

--rr-warning-soft       /* Warning background — Sunrise Orange family (#fff3e0) */
--rr-warning-soft-text  /* Warning text (#9a5b00) */
--rr-warning-soft-border/* Warning border */

--rr-success            /* Success — Mint Green (#2cb67d) */
--rr-success-soft       /* Success background (#e8f8f2) */
--rr-success-soft-text  /* Success text (#187a51) */
--rr-success-soft-border/* Success border */

--rr-info-soft          /* Information background — Sage Mist, NOT blue (#e8f2eb) */
--rr-info-soft-text     /* Information text (#176448) */
--rr-info-soft-border   /* Information border */

/* Focus & scrim */
--rr-focus              /* 3px focus ring, green (rgba(30, 122, 90, 0.55)) */
--rr-backdrop           /* Drawer / modal scrim */
```

### 📐 Spacing Rhythm (8px Base Scale)

| Token | Value | Pixel Equivalent | Typical Use Case |
| :--- | :--- | :--- | :--- |
| `--space-1` | `0.25rem` | 4px | Tight badges, chip inner spacing, icon gaps |
| `--space-2` | `0.5rem` | 8px | Button inline gap, form label margin |
| `--space-3` | `0.75rem` | 12px | Input padding, compact list items |
| `--space-4` | `1.0rem` | 16px | Card body padding, standard gutters |
| `--space-5` | `1.25rem` | 20px | Elevated card padding, section gap |
| `--space-6` | `1.5rem` | 24px | Grid gap, modal padding |
| `--space-8` | `2.0rem` | 32px | Page header margin, major section spacing |
| `--space-10` | `2.5rem` | 40px | Large hero spacing |
| `--space-12` | `3.0rem` | 48px | Page bottom padding |

### 🔲 Border Radius & Shadows

```css
--rr-radius-s: 6px;       /* Badges, chips, small inputs */
--rr-radius: 10px;        /* Standard buttons, dropdowns, inputs */
--rr-radius-l: 14px;      /* Cards, modals, major containers */
--rr-radius-pill: 999px;  /* Pill badges, avatar tags */

--rr-shadow: 0 1px 2px rgba(47, 52, 49, 0.05), 0 1px 3px rgba(47, 52, 49, 0.08);
--rr-shadow-pop: 0 8px 24px rgba(47, 52, 49, 0.14); /* Modals, dropdowns, toasts */
```

### ⚡ Motion & Timing

```css
--rr-ease: cubic-bezier(0.2, 0.7, 0.3, 1);
--rr-fast: 150ms;  /* Button hover, tab switch */
--rr-base: 240ms;  /* Card expand, modal reveal */
```

---

## 3. Typography Scale

RapidRelief uses **Lexend** (vendored in `wwwroot/fonts/`, variable weight 300–800) for maximum scan-readability during crisis operations.

| Scale | CSS Equivalent | Font Weight | Usage |
| :--- | :--- | :--- | :--- |
| **Page Title** | `h1` (1.5rem / 24px) | 700 (Bold) | Exactly one `<h1>` per page. |
| **Section Title** | `h2` (1.185rem / 19px) | 650 (Semi-bold) | Major card or panel header. |
| **Subsection** | `h3` (1.05rem / 17px) | 600 (Medium) | Subheaders, metric cards. |
| **Body** | `1rem` (16px) | 400 (Regular) | Primary content, incident descriptions. |
| **Small / Meta** | `0.875rem` (14px) | 400 (Regular) | Timestamp, helper text, table content. |
| **Caption** | `0.8125rem` (13px) | 500 (Medium) | Badges, status chips, field labels. |
| **Monospace** | `var(--rr-font-mono)` | 400 (Regular) | Incident IDs, GPS coordinates, codes. |

---

## 4. Reusable Blazor Component Catalog (`RapidRelief.Client.Common.Ui`)

All components are imported via `_Imports.razor` and available globally across all Razor files.

### 1. `RrButton`
Versatile, accessible button with built-in loading states, variants, and icon slots.
```razor
<RrButton Variant="ButtonVariant.Primary" 
          IconStart="send" 
          IsLoading="_isSubmitting" 
          OnClick="HandleSubmit">
    Submit Incident Report
</RrButton>

<RrButton Variant="ButtonVariant.Danger" IconStart="phone" Size="ButtonSize.Lg">
    Emergency Call (999)
</RrButton>
```

### 2. `RrCard`
Container with standard, interactive/hoverable, and highlight variants.
```razor
<RrCard Variant="CardVariant.Interactive" OnClick="() => NavigateTo(shelter.Id)">
    <Header>
        <div class="d-flex justify-content-between align-items-center">
            <h3>@shelter.Name</h3>
            <RrBadge Variant="BadgeVariant.Success" HasDot="true">Open</RrBadge>
        </div>
    </Header>
    <ChildContent>
        <p class="text-secondary">@shelter.Address</p>
        <div>Capacity: @shelter.Occupancy / @shelter.Capacity</div>
    </ChildContent>
</RrCard>
```

### 3. `RrBadge`
Status chip with soft background, semantic colors, and optional live beacon dot.
```razor
<RrBadge Variant="BadgeVariant.Danger" HasDot="true">Critical Severity</RrBadge>
<RrBadge Variant="BadgeVariant.Warning">Assigned</RrBadge>
<RrBadge Variant="BadgeVariant.Success">Resolved</RrBadge>
```

### 4. `RrAlert`
Semantic warning/notification banner with dismissible option.
```razor
<RrAlert Variant="AlertVariant.Danger" Title="Severe Flood Alert" Icon="alert-triangle">
    Evacuation in progress for Sector 4. Please proceed to the nearest shelter immediately.
</RrAlert>
```

### 5. `RrModal`
Accessible modal dialog with backdrop, escape key listener, and `aria-modal="true"`.
```razor
<RrModal IsOpen="_showModal" Title="Confirm Mission Assignment" OnClose="CloseModal">
    <ChildContent>
        <p>Assign Team Alpha to Incident #@_selectedId?</p>
    </ChildContent>
    <Footer>
        <RrButton Variant="ButtonVariant.Outline" OnClick="CloseModal">Cancel</RrButton>
        <RrButton Variant="ButtonVariant.Primary" OnClick="ConfirmAssignment">Confirm</RrButton>
    </Footer>
</RrModal>
```

### 6. `RrSkeleton`
Shimmer placeholder for content loading.
```razor
<!-- Text line skeleton -->
<RrSkeleton Shape="SkeletonShape.Text" Width="80%" />
<RrSkeleton Shape="SkeletonShape.Text" Width="60%" />

<!-- Card / Map placeholder skeleton -->
<RrSkeleton Shape="SkeletonShape.Rectangle" Height="240px" />
```

### 7. `RrEmptyState`
Standardized clean empty state with next-step action.
```razor
<RrEmptyState Icon="inbox" 
              Title="No Incidents Reported" 
              Description="There are currently no active emergency reports in this sector.">
    <ActionContent>
        <RrButton Variant="ButtonVariant.Primary" IconStart="plus" OnClick="OpenReportModal">
            Submit New Report
        </RrButton>
    </ActionContent>
</RrEmptyState>
```

### 8. `RrLoadingState`
Accessible loading container with `aria-busy="true"` and live status update.
```razor
<RrLoadingState Message="Connecting to emergency response grid..." />
```

### 9. `RrErrorState`
User-friendly error display explaining what went wrong and offering recovery actions.
```razor
<RrErrorState Title="Unable to Load Live Incidents" 
              Message="Network request timed out. You can retry or work in offline mode." 
              OnRetry="LoadDataAsync" />
```

### 10. `RrStatusIndicator`
Live connection and system status beacon (Online, Offline, Degraded, Active).
```razor
<RrStatusIndicator Status="SystemStatus.Online" Text="Real-Time Grid Connected" Pulsing="true" />
<RrStatusIndicator Status="SystemStatus.Degraded" Text="Demo Mode (Local Data)" />
```

### 11. `RrTabs` & `RrTabItem`
Keyboard-accessible tab bar.
```razor
<RrTabs ActiveKey="@_activeTab" ActiveKeyChanged="key => _activeTab = key">
    <RrTabItem Key="map" Label="Live Map" Icon="map" BadgeCount="12">
        <RapidMap Incidents="@_incidents" />
    </RrTabItem>
    <RrTabItem Key="list" Label="Incident List" Icon="activity">
        <IncidentTable Incidents="@_incidents" />
    </RrTabItem>
</RrTabs>
```

### 12. `RrInput`
Accessible form control with integrated label, prefix/suffix icon slots, helper text, and validation message.
```razor
<RrInput Label="Contact Phone Number" 
         Type="tel" 
         @bind-Value="_phone" 
         IconStart="phone" 
         Placeholder="+880 17..." 
         HelperText="Used solely for rescue dispatch confirmation." />
```

---

## 5. Responsive Design & Breakpoints

RapidRelief uses a mobile-first responsive strategy:

```text
320px – 480px   👉 Mobile Phone (Stacked full-width cards, large touch targets ≥44px)
481px – 899px   👉 Tablet / Phablet (Drawer sidebar, 2-column grids)
900px – 1100px  👉 Laptop / Standard Desktop (Fixed sidebar, full tables, split map-list)
1200px+         👉 Wide Command Center (Multi-column live operations grid)
```

### Rules for Mobile Emergency UX:
- Minimum touch target: **44px × 44px**.
- Critical actions (SOS, Call 999, Submit Report) must be visible above the fold.
- Tables collapse gracefully or scroll horizontally within `.rr-table-wrap`.
- Maps provide zoom controls accessible with one thumb.

---

## 6. Accessibility Standards (WCAG 2.1 AA)

1. **Focus Rings:** All interactive elements must show `:focus-visible` with a high-contrast 3px outline (`--rr-focus`).
2. **Keyboard Navigation:** Every modal, tab, and button must be navigable with `Tab`, `Shift+Tab`, `Space`, `Enter`, and `Escape`.
3. **Screen Readers:**
   - Icon-only buttons must have `aria-label`.
   - Live updates must use `aria-live="polite"`.
   - Asynchronous loading areas must declare `aria-busy="true"`.
4. **Color Independence:** Never use color alone to convey status. Always pair colors with clear text or an icon (e.g., green dot + "Open", red badge + "Critical").
5. **Reduced Motion:** All animations automatically stop or simplify under `@media (prefers-reduced-motion: reduce)`.

---

## 7. Map UX Guidelines (Leaflet + OpenStreetMap)

- All Leaflet maps must be encapsulated in `<RapidMap>` with a minimum height of `380px` on mobile and `480px` on desktop.
- Markers must pair a **token** color with a text label — never a raw hex, never color alone:
  - Critical/High: `--rr-danger` (Rescue Red)
  - Medium/Warning: `--rr-warning-soft-text` (Sunrise Orange family)
  - Low/Info: `--rr-primary` (Forest Green) — the old blue is retired (D-074)
  - Resolved/Safe: `--rr-success` (Mint)
- The user's own position is **not** a marker. Pass `UserLocation` + `UserLocationAccuracyMeters` to `<RapidMap>`; it renders a dot + accuracy halo (`.rapid-map-user-dot` / `.rapid-map-user-halo`, `--rr-primary`) that the marker diff cannot erase (D-075).
- Get coordinates from the injected `GeolocationService` — never from `navigator.geolocation` directly. It never throws; on failure render `result.Message` with a Retry action and keep a sensible map fallback (see [design.md](design.md) §10).
- Popups must contain: Title, Severity Badge, Address/Coordinates, and a primary CTA ("View Incident" or "Navigate").

---

## 8. Definition of Done for Any Frontend Pull Request

Before marking any UI task complete, verify:
- [ ] Rendered and verified in **Light Mode** AND **Dark Mode**.
- [ ] Tested on **375px mobile** and **desktop** (zero horizontal scroll).
- [ ] Fully navigable via **Keyboard** (all interactive controls reachable, focus ring visible).
- [ ] Handles **Loading**, **Empty**, **Error**, and **Degraded/Offline** states.
- [ ] Uses only semantic tokens (`--rr-*`) — zero raw hex colors.
- [ ] No raw HTML / `MarkupString` usage (`ClientRenderSafetyTests` pass).
- [ ] `dotnet build RapidRelief.sln` and `dotnet test RapidRelief.sln` pass 100% green.
