# Implementation notes

- The widget renders in `HomepageBeforeProducts`. Its plugin-owned stylesheet hides only the Venture theme's native category and featured-job wrappers so this cohesive section replaces their visible intent without changing theme or core files.
- Category identifiers never enter rendered markup. Pills use localized names as labels and the public `SeName` in HTMX routes; the controller resolves the authorized internal category identifier server-side.
- When the installed `Misc.AIInterview` plugin is available, products are restricted to its job product template and rendered through the named `AIInterviewJobProductCard` component. Without it, native secure catalog results use the plugin's deliberate compact fallback card.
- nopCommerce's bundled HTMX asset is loaded first. The plugin-owned `htmx.min.js` copy is used by the script tag's `onerror` fallback, with no CDN dependency.
- Product lookup uses native catalog search with the current store, published/availability rules, ACL rules, visible-individually filtering, and native product overview model preparation.
