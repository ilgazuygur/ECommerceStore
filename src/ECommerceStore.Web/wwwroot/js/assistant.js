(function () {
    "use strict";

    document.addEventListener("DOMContentLoaded", function () {
        var widget = document.querySelector("[data-assistant-widget]");
        if (!widget) return;

        var openButton = widget.querySelector("[data-assistant-open]");
        var panel = widget.querySelector(".assistant-panel");
        var backdrop = widget.querySelector(".assistant-backdrop");
        var closeButtons = widget.querySelectorAll("[data-assistant-close]");
        var newButton = widget.querySelector("[data-assistant-new]");
        var conversations = widget.querySelector("[data-assistant-conversations]");
        var messages = widget.querySelector("[data-assistant-messages]");
        var status = widget.querySelector("[data-assistant-status]");
        var form = widget.querySelector("[data-assistant-form]");
        var input = widget.querySelector("[data-assistant-input]");
        var authenticated = widget.dataset.authenticated === "true";
        var currentConversationId = null;
        var lastFocused = null;
        var initialized = false;
        var submitting = false;

        function setOpen(value) {
            if (value) {
                lastFocused = document.activeElement;
                panel.hidden = false;
                backdrop.hidden = false;
                openButton.setAttribute("aria-expanded", "true");
                document.body.classList.add("assistant-open");
                if (!initialized) initialize();
                window.setTimeout(function () { (authenticated ? input : panel.querySelector("a,button"))?.focus(); }, 0);
            } else {
                panel.hidden = true;
                backdrop.hidden = true;
                openButton.setAttribute("aria-expanded", "false");
                document.body.classList.remove("assistant-open");
                if (lastFocused && lastFocused.focus) lastFocused.focus();
            }
        }

        async function initialize() {
            initialized = true;
            if (!authenticated) {
                renderSignedOut();
                return;
            }
            await loadConversations(true);
        }

        function renderSignedOut() {
            conversations.replaceChildren();
            messages.replaceChildren();
            form.hidden = true;
            newButton.hidden = true;
            var empty = element("div", "assistant-empty");
            empty.append(element("span", "assistant-empty-mark", "✦"));
            empty.append(element("h3", "", "Your personal shopping guide"));
            empty.append(element("p", "", "Sign in to save conversations and get recommendations grounded in the live catalogue."));
            var link = element("a", "assistant-signin", "Sign in to continue");
            link.href = widget.dataset.loginUrl + "?returnUrl=" + encodeURIComponent(window.location.pathname + window.location.search);
            empty.append(link);
            messages.append(empty);
        }

        async function request(url, options) {
            var settings = options || {};
            settings.headers = settings.headers || {};
            settings.headers.Accept = "application/json";
            if (settings.method && settings.method !== "GET") {
                settings.headers["Content-Type"] = "application/json";
                var token = form.querySelector('input[name="__RequestVerificationToken"]');
                if (token) settings.headers.RequestVerificationToken = token.value;
            }
            var response = await fetch(url, settings);
            if (response.status === 401) {
                authenticated = false;
                renderSignedOut();
                throw new Error("Sign in to use the assistant.");
            }
            var contentType = response.headers.get("content-type") || "";
            var data = contentType.indexOf("json") >= 0 ? await response.json() : null;
            if (!response.ok && response.status !== 202) {
                var error = new Error((data && (data.detail || data.title)) || "The assistant request failed.");
                error.status = response.status;
                throw error;
            }
            return { response: response, data: data };
        }

        async function loadConversations(selectFirst) {
            setStatus("Loading conversations…", true);
            try {
                var result = await request("/api/assistant/conversations");
                renderConversationList(result.data || []);
                if (selectFirst) {
                    if (result.data && result.data.length) await selectConversation(result.data[0].id);
                    else await createConversation();
                }
                setStatus("");
            } catch (error) {
                setStatus(error.message, false, true);
            }
        }

        function renderConversationList(items) {
            conversations.replaceChildren();
            if (!items.length) {
                conversations.append(element("p", "assistant-sidebar-empty", "No saved chats yet."));
                return;
            }
            items.forEach(function (item) {
                var row = element("div", "assistant-conversation-row");
                if (item.id === currentConversationId) row.classList.add("is-active");
                var choose = element("button", "assistant-conversation", item.title);
                choose.type = "button";
                choose.addEventListener("click", function () { selectConversation(item.id); });
                var remove = element("button", "assistant-conversation-delete", "×");
                remove.type = "button";
                remove.setAttribute("aria-label", "Delete " + item.title);
                remove.addEventListener("click", function () { deleteConversation(item.id); });
                row.append(choose, remove);
                conversations.append(row);
            });
        }

        async function createConversation() {
            if (!authenticated) return;
            setStatus("Starting a new conversation…", true);
            try {
                var result = await request("/api/assistant/conversations", { method: "POST", body: "{}" });
                currentConversationId = result.data.id;
                renderMessages([]);
                await loadConversations(false);
                input.focus();
            } catch (error) {
                setStatus(error.message, false, true);
            }
        }

        async function selectConversation(id) {
            setStatus("Loading messages…", true);
            try {
                var result = await request("/api/assistant/conversations/" + encodeURIComponent(id));
                currentConversationId = id;
                renderMessages(result.data.messages || []);
                await loadConversations(false);
                setStatus("");
            } catch (error) {
                setStatus(error.status === 404 ? "That conversation is no longer available." : error.message, false, true);
            }
        }

        async function deleteConversation(id) {
            if (!window.confirm("Delete this conversation?")) return;
            try {
                await request("/api/assistant/conversations/" + encodeURIComponent(id), { method: "DELETE" });
                if (currentConversationId === id) currentConversationId = null;
                await loadConversations(true);
            } catch (error) {
                setStatus(error.message, false, true);
            }
        }

        function renderMessages(items) {
            messages.replaceChildren();
            if (!items.length) {
                var empty = element("div", "assistant-empty");
                empty.append(element("span", "assistant-empty-mark", "✦"));
                empty.append(element("h3", "", "What are you looking for?"));
                empty.append(element("p", "", "Try “Show me in-stock electronics under $100” or ask about a previous recommendation."));
                messages.append(empty);
            } else {
                items.forEach(renderMessage);
            }
            scrollToBottom();
        }

        function renderMessage(message) {
            var article = element("article", "assistant-message assistant-message-" + message.role);
            article.append(element("div", "assistant-message-label", message.role === "user" ? "You" : "Assistant"));
            article.append(element("p", "assistant-message-text", message.content));
            if (message.products && message.products.length) {
                var grid = element("div", "assistant-product-grid");
                message.products.forEach(function (product) { grid.append(renderProduct(product)); });
                article.append(grid);
            }
            messages.append(article);
        }

        function renderProduct(product) {
            var card = element("article", "assistant-product-card" + (product.isUnavailable ? " is-unavailable" : ""));
            if (product.isUnavailable) {
                card.append(element("div", "assistant-product-unavailable", "Currently unavailable"));
                card.append(element("h4", "", product.name));
                card.append(element("p", "", "This previously suggested product is no longer in the public catalogue."));
                return card;
            }
            var imageLink = element("a", "assistant-product-image");
            imageLink.href = product.productUrl;
            var image = document.createElement("img");
            image.alt = "";
            image.loading = "lazy";
            image.src = product.imageUrl || "/images/product-placeholder.svg";
            image.dataset.fallback = "/images/product-placeholder.svg";
            imageLink.append(image);
            var copy = element("div", "assistant-product-copy");
            copy.append(element("span", "assistant-product-category", product.category || "Catalogue"));
            var title = element("a", "assistant-product-title", product.name);
            title.href = product.productUrl;
            copy.append(title);
            copy.append(element("strong", "assistant-product-price", formatMoney(product.price, product.currencyCode)));
            copy.append(element("span", "assistant-product-stock", product.stockQuantity > 0 ? "In stock" : "Out of stock"));
            card.append(imageLink, copy);
            return card;
        }

        function formatMoney(value, currency) {
            try { return new Intl.NumberFormat(undefined, { style: "currency", currency: currency || "USD" }).format(value); }
            catch (_) { return (currency || "USD") + " " + Number(value).toFixed(2); }
        }

        async function submitMessage(event) {
            event.preventDefault();
            var text = input.value.trim();
            if (!text || !currentConversationId || submitting) return;
            submitting = true;
            input.value = "";
            var requestId = createUuid();
            var optimistic = { role: "user", content: text, products: [] };
            if (messages.querySelector(".assistant-empty")) messages.replaceChildren();
            renderMessage(optimistic);
            var typing = element("div", "assistant-typing", "Assistant is checking the catalogue…");
            messages.append(typing);
            scrollToBottom();
            await sendWithPolling(text, requestId, typing);
            submitting = false;
        }

        async function sendWithPolling(text, requestId, typing) {
            try {
                for (var poll = 0; poll < 45; poll += 1) {
                    var result = await request("/api/assistant/conversations/" + encodeURIComponent(currentConversationId) + "/messages", {
                        method: "POST",
                        body: JSON.stringify({ clientRequestId: requestId, message: text })
                    });
                    if (result.response.status === 202) {
                        await delay((result.data && result.data.retryAfterMilliseconds) || 800);
                        continue;
                    }
                    typing.remove();
                    if (result.data && result.data.message) renderMessage(result.data.message);
                    await loadConversations(false);
                    setStatus("");
                    scrollToBottom();
                    return;
                }
                throw new Error("The request is still processing. Try again shortly.");
            } catch (error) {
                typing.remove();
                var errorRow = element("div", "assistant-send-error");
                errorRow.append(element("span", "", error.status === 429 ? "Too many requests. Wait a moment." : error.message));
                var retry = element("button", "", "Retry safely");
                retry.type = "button";
                retry.addEventListener("click", function () {
                    errorRow.replaceWith(typing);
                    sendWithPolling(text, requestId, typing);
                });
                errorRow.append(retry);
                messages.append(errorRow);
                scrollToBottom();
            }
        }

        function setStatus(text, busy, isError) {
            status.textContent = text || "";
            status.classList.toggle("is-visible", Boolean(text));
            status.classList.toggle("is-busy", Boolean(busy));
            status.classList.toggle("is-error", Boolean(isError));
        }

        function scrollToBottom() { messages.scrollTop = messages.scrollHeight; }
        function delay(ms) { return new Promise(function (resolve) { window.setTimeout(resolve, ms); }); }
        function createUuid() {
            if (window.crypto && window.crypto.randomUUID) return window.crypto.randomUUID();
            var bytes = new Uint8Array(16);
            window.crypto.getRandomValues(bytes);
            bytes[6] = (bytes[6] & 15) | 64;
            bytes[8] = (bytes[8] & 63) | 128;
            return Array.from(bytes, function (byte, index) {
                return ([4, 6, 8, 10].indexOf(index) >= 0 ? "-" : "") + byte.toString(16).padStart(2, "0");
            }).join("");
        }
        function element(tag, className, text) {
            var node = document.createElement(tag);
            if (className) node.className = className;
            if (text !== undefined) node.textContent = text;
            return node;
        }

        openButton.addEventListener("click", function () { setOpen(true); });
        closeButtons.forEach(function (button) { button.addEventListener("click", function () { setOpen(false); }); });
        newButton.addEventListener("click", createConversation);
        form.addEventListener("submit", submitMessage);
        input.addEventListener("keydown", function (event) {
            if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); form.requestSubmit(); }
        });
        panel.addEventListener("keydown", function (event) {
            if (event.key === "Escape") { event.preventDefault(); setOpen(false); return; }
            if (event.key !== "Tab") return;
            var focusable = Array.from(panel.querySelectorAll('button:not([disabled]),a[href],textarea:not([disabled]),input:not([disabled])')).filter(function (item) {
                return !item.hidden && item.getClientRects().length > 0;
            });
            if (!focusable.length) return;
            var first = focusable[0];
            var last = focusable[focusable.length - 1];
            if (event.shiftKey && document.activeElement === first) { event.preventDefault(); last.focus(); }
            else if (!event.shiftKey && document.activeElement === last) { event.preventDefault(); first.focus(); }
        });
    });
})();
