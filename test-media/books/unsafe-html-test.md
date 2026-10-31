# Unsafe HTML test

This file tests the XSS guard. With **AllowRawHtml = false** (default) the block
below must render as **plain text**, and no script may execute in the browser
(DevTools console must stay clean):

<script>alert('XSS via script tag')</script>

<img src="x" onerror="alert('XSS via onerror')">

<iframe src="https://example.com"></iframe>

javascript: should only work when the admin enables AllowRawHtml, at their own
risk on single-user servers.

# Safe formatting still works

- **Bold** should still render
- `code` should still render
- [A normal link](https://jellyfin.org) should still render