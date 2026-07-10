// Progressive enhancement only. Every action below has a server-side fallback,
// and no inline script or inline event handler is used, so a strict
// Content-Security-Policy (script-src 'self') can be enforced.

(function () {
    "use strict";

    // Missing/broken product images fall back to a bundled placeholder.
    // The 'error' event does not bubble, so listen during the capture phase.
    document.addEventListener("error", function (event) {
        var el = event.target;
        if (el && el.tagName === "IMG" && el.dataset && el.dataset.fallback && !el.dataset.fellBack) {
            el.dataset.fellBack = "1";
            el.src = el.dataset.fallback;
        }
    }, true);

    // Destructive forms confirm before submitting: <form data-confirm="Are you sure?">.
    document.addEventListener("submit", function (event) {
        var form = event.target;
        if (form && form.dataset && form.dataset.confirm && !window.confirm(form.dataset.confirm)) {
            event.preventDefault();
        }
    }, true);

    // Controls marked data-autosubmit submit their form on change; a visible
    // submit button remains for browsers without JavaScript.
    document.addEventListener("change", function (event) {
        var el = event.target;
        if (el && el.hasAttribute && el.hasAttribute("data-autosubmit") && el.form) {
            el.form.submit();
        }
    });

    // Optional admin AI draft. The button is only rendered when AI is enabled and
    // the user is an administrator; the draft is inserted for review and never saved.
    document.addEventListener("DOMContentLoaded", function () {
        var button = document.getElementById("ai-draft-btn");
        if (!button) {
            return;
        }

        button.addEventListener("click", async function () {
            var url = button.dataset.draftUrl;
            var target = document.getElementById(button.dataset.target || "FullDescription");
            if (!url || !target) {
                return;
            }

            var name = (document.getElementById("Name") || {}).value || "";
            var categorySelect = document.getElementById("CategoryId");
            var category = categorySelect ? (categorySelect.options[categorySelect.selectedIndex] || {}).text || "" : "";
            var tokenField = document.querySelector('input[name="__RequestVerificationToken"]');
            var token = tokenField ? tokenField.value : "";

            var body = new URLSearchParams({ name: name, category: category, __RequestVerificationToken: token });
            button.disabled = true;
            try {
                var response = await fetch(url, {
                    method: "POST",
                    headers: { "Content-Type": "application/x-www-form-urlencoded" },
                    body: body
                });
                var data = await response.json();
                if (data.available && data.draft) {
                    target.value = data.draft;
                }
            } catch (error) {
                // The draft is optional; manual entry always works.
            } finally {
                button.disabled = false;
            }
        });
    });
})();
