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
2. **Never use red for ordinary actions.** `--rr-primary` (Safety Blue) is for primary actions. `--rr-danger` (Emergency Red) is **strictly reserved** for emergencies, critical alerts, SOS, and destructive actions.
3. **Never use emojis as icons.** Always use `<AppIcon Name="..." />`.
4. **Never render raw HTML (`MarkupString`, `innerHTML`, `outerHTML`, or markdown renderers).** All content must use Blazor's `@-interpolation` to prevent XSS (enforced by `ClientRenderSafetyTests`).
5. **No decorative glassmorphism or 3D clutter.** The header is the only translucent surface (`--rr-backdrop`). Everything else uses clean, solid surface tokens.

---

## 2. Design Token System

All components must strictly use tokens defined on `:root` and `[data-theme='dark']` in `src/RapidRelief.Client/wwwroot/css/app.css`.

### 🎨 Semantic Colors & Surfaces

```css
/* Surface Tokens */
--rr-bg                 /* Main background (Light: #f6f7f9 | Dark: #0b1220) */
--rr-surface            /* Card / Container surface (Light: #ffffff | Dark: #121b2e) */
--rr-surface-2          /* Elevated section / muted background (Light: #eef1f5 | Dark: #182338) */
--rr-surface-3          /* Hover / active surface (Light: #e3e8ef | Dark: #203050) */

/* Text & Foreground */
--rr-text               /* High-contrast primary text (Light: #0f172a | Dark: #edf2f9) */
--rr-text-2             /* Secondary body text (Light: #46556b | Dark: #a9b7cd) */
--rr-text-3             /* Muted captions & metadata (Light: #64748b | Dark: #8294ae) */

/* Borders */
--rr-border             /* Standard subtle border */
--rr-border-strong      /* High-contrast card & input border */

/* Actions & Brand */
--rr-primary            /* Primary button & brand action (#2563eb) */
--rr-primary-hover      /* Hover state for primary action */
--rr-on-primary         /* Text color on primary button (#ffffff) */
--rr-link               /* Hyperlink color */

/* Severity & Semantic Soft Pairs */
--rr-danger             /* Critical alarm, emergency, destructive (#dc2626 / #ef4444) */
--rr-danger-soft        /* Soft emergency banner background */
--rr-danger-soft-text   /* Soft emergency text */
--rr-danger-soft-border /* Soft emergency border */

--rr-warning-soft       /* Warning background (amber/yellow) */
--rr-warning-soft-text  /* Warning text */
--rr-warning-soft-border/* Warning border */

--rr-success            /* Success green (#16a34a / #22c55e) */
--rr-success-soft       /* Success background */
--rr-success-soft-text  /* Success text */
--rr-success-soft-border/* Success border */

--rr-info-soft          /* Information blue background */
--rr-info-soft-text     /* Information text */
--rr-info-soft-border   /* Information border */
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

--rr-shadow: 0 1px 2px rgba(15, 23, 42, 0.05), 0 1px 3px rgba(15, 23, 42, 0.08);
--rr-shadow-pop: 0 8px 24px rgba(15, 23, 42, 0.14); /* Modals, dropdowns, toasts */
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
- Markers must use high-contrast color pins corresponding to incident severity:
  - Critical/High: Red (`#ef4444`)
  - Medium/Warning: Amber (`#f59e0b`)
  - Low/Info: Blue (`#3b82f6`)
  - Resolved: Green (`#22c55e`)
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
