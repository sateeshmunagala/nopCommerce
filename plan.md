1. **Understand Goal**: Apply Venture-inspired styling and behaviors to the header, search, mobile drawer, and footer of the `JobBoardVenture` theme.
2. **Key Constraints**:
   - Work ONLY inside `src/Presentation/Nop.Web/Themes/JobBoardVenture`.
   - Preserve native markup structure as much as possible, mostly writing CSS and JS.
   - Use CSS custom properties: `--jb-bg`, `--jb-ink`, `--jb-muted`, `--jb-accent`, `--jb-accent-dark`, `--jb-white`, `--jb-border`.
   - Fonts: 'Fjalla One' for headings/menu, 'Lato' for body.
3. **Tasks**:
   - **CSS (`jobboard-venture.css`)**:
     - Desktop header: `var(--jb-white)` background, compact (64px), logo left, menu centered, search/account right.
     - Mobile header: Compact top bar, hamburger menu, logo, search icon, cart.
     - Mobile drawer: Left off-canvas menu for navigation, includes account links if possible.
     - Search overlay: Mobile search panel dropping from the header, focused when opened.
     - Footer: Desktop column layout, mobile accordion layout (styled via existing accordion behavior or custom).
     - Add necessary `.jb-menu-open`, `.jb-search-open`, `.jb-footer-open` state classes.
   - **JavaScript (`jobboard-venture.js`)**:
     - Handle mobile menu toggle (adding `.jb-menu-open` to body or a wrapper).
     - Handle search toggle (adding `.jb-search-open`).
     - Handle footer accordion (if native JS doesn't suffice or if we want to override default behavior).
   - **Razor Views (optional but may be needed to add IDs/classes or restructure slightly for the drawer)**:
     - I've copied `_Header.cshtml`, `_Root.cshtml`, `HeaderLinks/Default.cshtml`, `SearchBox/Default.cshtml`, and `Footer/Default.cshtml` to the theme so we can add small helper hooks (e.g., icons, buttons for mobile toggle) if they don't exist in native markup.
     - We should keep edits to these views minimal.
