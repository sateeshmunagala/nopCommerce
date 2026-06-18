I need to fix the two blocking issues:
1. Desktop Header Missing Links: `.header-upper` was set to `display: none;`, which hid the account/cart links. I need to make those links appear in the header right area on desktop, perhaps by unhiding `.header-upper` on desktop, or positioning it next to the search box, while keeping it hidden on mobile (since mobile uses the drawer). Let's revise the CSS to display `.header-upper` horizontally aligned with `.header-lower` on desktop, or restructure `_Header.cshtml` to place the `HeaderLinksViewComponent` in `.header-lower` on desktop. Given the prompt constraint "Prefer icon-like compact controls where the existing markup supports it" and the desire for "Search/account/cart links to the right", I'll move `.header-links-wrapper` into `.header-lower` and ensure it's visible on desktop.
2. Mobile Footer Accordion Broken: The `.jb-footer-open` class toggles the plus/minus but doesn't actually display the `.listbox`. I need to add `.footer-block.jb-footer-open .listbox { display: block; }`. Also, the default script in `Footer/Default.cshtml` targets `.block .title`, which doesn't match `.footer-block .title`. The prompt says "mostly writing CSS and JS... reuse existing...". I should modify the CSS to show the `.listbox`.

Plan:
1. Edit `_Header.cshtml` to move the `.header-links-wrapper` down into the `.header-lower` div for desktop layout.
2. Edit `jobboard-venture.css` to fix the `.header-upper` display, arrange `.header-links-wrapper`, and fix the `.listbox` display for the footer accordion.
3. Test locally by running builds.
4. Verify changes via code review again.
