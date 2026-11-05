/**
 * PocketGoal AI Chatbot Client Script
 */
(function () {
    const STORAGE_KEY_HISTORY = 'pg_chat_history';
    const STORAGE_KEY_IS_OPEN = 'pg_chat_is_open';

    const widget = document.getElementById('pgChatWidget');
    if (!widget) return;

    const toggleBtn = document.getElementById('pgChatToggleBtn');
    const panel = document.getElementById('pgChatPanel');
    const closeBtn = document.getElementById('pgChatCloseBtn');
    const clearBtn = document.getElementById('pgChatClearBtn');
    const messagesContainer = document.getElementById('pgChatMessages');
    const suggestionsContainer = document.getElementById('pgChatSuggestions');
    const form = document.getElementById('pgChatForm');
    const input = document.getElementById('pgChatInput');
    const sendBtn = document.getElementById('pgChatSendBtn');

    const iconClosed = toggleBtn?.querySelector('.launcher-icon-closed');
    const iconOpened = toggleBtn?.querySelector('.launcher-icon-opened');

    let history = [];
    const activeRequests = new Map(); // msgId -> { controller, timerId, userText, pendingEl }

    // Helper: escape HTML to prevent XSS
    function escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Helper: extract clean domain name from URL
    function extractDomain(url) {
        if (!url) return 'store page';
        try {
            const parsed = new URL(url);
            return parsed.hostname.replace(/^www\./i, '');
        } catch {
            return 'store page';
        }
    }

    // Helper: get current time formatted
    function getCurrentTime() {
        const d = new Date();
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
    }

    // Always ensure the send button stays enabled and has only its icon
    function ensureSendButtonClean() {
        if (!sendBtn) return;
        sendBtn.disabled = false;
        sendBtn.removeAttribute('disabled');
        sendBtn.classList.remove('is-submitting');
        sendBtn.removeAttribute('aria-busy');
        if (sendBtn.innerHTML.trim() !== '<i class="bi bi-send-fill"></i>') {
            sendBtn.innerHTML = '<i class="bi bi-send-fill"></i>';
        }
    }

    // Smart scroll: only auto-scroll if user is already near the bottom
    function smartScrollToBottom(force = false) {
        if (!messagesContainer) return;
        const distanceFromBottom = messagesContainer.scrollHeight - messagesContainer.scrollTop - messagesContainer.clientHeight;
        if (force || distanceFromBottom < 80) {
            setTimeout(() => {
                if (messagesContainer) {
                    messagesContainer.scrollTop = messagesContainer.scrollHeight;
                }
            }, 30);
        }
    }

    // Load history from session storage
    function loadHistory() {
        try {
            const stored = sessionStorage.getItem(STORAGE_KEY_HISTORY);
            if (stored) {
                history = JSON.parse(stored);
            }
        } catch (e) {
            console.warn('Failed to parse chat history from storage:', e);
            history = [];
        }

        if (!history || history.length === 0) {
            renderWelcomeMessage();
        } else {
            renderAllMessages();
        }
    }

    function saveHistory() {
        try {
            if (history.length > 25) {
                history = history.slice(-25);
            }
            sessionStorage.setItem(STORAGE_KEY_HISTORY, JSON.stringify(history));
        } catch (e) {
            console.warn('Failed to save chat history:', e);
        }
    }

    function toggleChat(forceOpen) {
        if (!panel) return;
        const willOpen = forceOpen !== undefined ? forceOpen : panel.classList.contains('d-none');

        if (willOpen) {
            panel.classList.remove('d-none');
            iconClosed?.classList.add('d-none');
            iconOpened?.classList.remove('d-none');
            sessionStorage.setItem(STORAGE_KEY_IS_OPEN, 'true');
            smartScrollToBottom(true);
            setTimeout(() => input?.focus(), 100);
        } else {
            panel.classList.add('d-none');
            iconClosed?.classList.remove('d-none');
            iconOpened?.classList.add('d-none');
            sessionStorage.setItem(STORAGE_KEY_IS_OPEN, 'false');
        }
    }

    function renderWelcomeMessage() {
        messagesContainer.innerHTML = '';
        const welcomeHtml = `
            <div class="pg-chat-msg bot">
                <div class="pg-bubble">
                    <p class="mb-1 fw-bold text-primary"><i class="bi bi-stars me-1"></i> PocketGoal AI</p>
                    <p class="mb-1">Hello! I'm your AI financial assistant. You can manage your finances naturally by talking to me:</p>
                    <ul class="mb-0 ps-3 small text-muted">
                        <li>"Add expense: lunch, 250"</li>
                        <li>"Suggest a laptop under 60000"</li>
                        <li>"iPad Air 11 inch price theriyala, search pannu"</li>
                        <li>"How much did I spend this month?"</li>
                        <li>"What is my total savings?"</li>
                    </ul>
                </div>
                <div class="pg-msg-time">${getCurrentTime()}</div>
            </div>
        `;
        messagesContainer.insertAdjacentHTML('beforeend', welcomeHtml);
        smartScrollToBottom(true);
    }

    function renderAllMessages() {
        messagesContainer.innerHTML = '';
        history.forEach(item => {
            appendMessageBubble(
                item.role,
                item.content,
                item.time,
                item.saved,
                item.record,
                item.suggestions,
                item.isLive,
                item.priceLookup,
                item.cardStatus
            );
        });
        smartScrollToBottom(true);
    }

    function appendMessageBubble(role, content, time, saved, record, suggestions, isLive, priceLookup, cardStatus) {
        const isUser = role === 'user';
        const msgDiv = document.createElement('div');
        msgDiv.className = `pg-chat-msg ${isUser ? 'user' : 'bot'}`;

        let recordBadgeHtml = '';
        if (saved && record) {
            let label = 'Recorded successfully';
            if (record.type === 'expense') {
                label = `Expense logged: ₹${record.amount} (${record.category || 'Expense'})`;
            } else if (record.type === 'goal') {
                label = `Goal created: ${record.name || 'Goal'} (₹${record.targetAmount})`;
            } else if (record.type === 'saving') {
                label = `Savings updated: ₹${record.amount} added to ${record.goalName || 'Goal'}`;
            }
            recordBadgeHtml = `<div class="pg-record-badge"><i class="bi bi-check-circle-fill"></i> ${escapeHtml(label)}</div>`;
        }

        // Render product suggestion cards if provided
        let suggestionsHtml = '';
        if (suggestions && Array.isArray(suggestions) && suggestions.length > 0) {
            let cardsHtml = '';
            suggestions.forEach((item, idx) => {
                const safeName = escapeHtml(item.name);
                const safeReason = escapeHtml(item.reason);
                const priceFormatted = Number(item.approxPrice || 0).toLocaleString('en-IN');

                let viewLinkHtml = '';
                if (item.sourceUrl && item.sourceUrl.startsWith('https://')) {
                    const domain = extractDomain(item.sourceUrl);
                    viewLinkHtml = `
                        <a href="${encodeURI(item.sourceUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-outline-primary" title="View official source">
                            <i class="bi bi-box-arrow-up-right me-1"></i> ${escapeHtml(domain)}
                        </a>
                    `;
                }

                let amazonSearchHtml = '';
                if (item.searchUrl) {
                    amazonSearchHtml = `
                        <a href="${encodeURI(item.searchUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-outline-secondary" title="Search on Amazon India">
                            <i class="bi bi-cart me-1"></i> Amazon
                        </a>
                    `;
                }

                let flipkartSearchHtml = '';
                if (item.flipkartUrl) {
                    flipkartSearchHtml = `
                        <a href="${encodeURI(item.flipkartUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-outline-secondary" title="Search on Flipkart">
                            <i class="bi bi-bag me-1"></i> Flipkart
                        </a>
                    `;
                }

                cardsHtml += `
                    <div class="pg-suggestion-card" data-card-idx="${idx}">
                        <div class="pg-suggestion-title">${safeName}</div>
                        <div class="pg-suggestion-price-row">
                            <span class="pg-price-approx">approx.</span>
                            <span class="pg-suggestion-price">₹${priceFormatted}</span>
                        </div>
                        <div class="pg-suggestion-reason">${safeReason}</div>
                        <div class="pg-suggestion-actions">
                            <button type="button" class="btn btn-primary pg-touch-btn w-100 pg-btn-set-goal" data-name="${safeName}" data-price="${item.approxPrice}">
                                <i class="bi bi-bullseye me-1"></i> Set as goal
                            </button>
                            <div class="pg-suggestion-links">
                                ${viewLinkHtml}
                                ${amazonSearchHtml}
                                ${flipkartSearchHtml}
                            </div>
                        </div>
                    </div>
                `;
            });

            const noteText = isLive === false
                ? 'These are AI suggestions, not live prices. Please check the price on the store page.'
                : 'Prices are approximate and may change.';

            suggestionsHtml = `
                <div class="pg-suggestions-container">
                    ${cardsHtml}
                    <div class="pg-suggestion-note"><i class="bi bi-info-circle me-1"></i> ${escapeHtml(noteText)}</div>
                </div>
            `;
        }

        // Render Price Lookup Confirmation Card if provided
        let priceLookupHtml = '';
        if (priceLookup && typeof priceLookup === 'object') {
            const safeName = escapeHtml(priceLookup.productName);
            const priceFormatted = Number(priceLookup.approxPrice || 0).toLocaleString('en-IN');
            const safeVariant = escapeHtml(priceLookup.variantNote || '');
            const isLivePrice = priceLookup.isLive !== false;

            let sourceLinkHtml = '';
            if (priceLookup.sourceUrl && priceLookup.sourceUrl.startsWith('https://')) {
                const domain = extractDomain(priceLookup.sourceUrl);
                sourceLinkHtml = `
                    <div class="pg-price-source mb-1">
                        Source: <a href="${encodeURI(priceLookup.sourceUrl)}" target="_blank" rel="noopener noreferrer" class="text-primary text-decoration-none fw-semibold">
                            <i class="bi bi-box-arrow-up-right me-1"></i> ${escapeHtml(domain)}
                        </a>
                    </div>
                `;
            }

            let actionsHtml = '';
            if (cardStatus === 'created') {
                actionsHtml = `<div class="pg-card-status-line status-success"><i class="bi bi-check-circle-fill me-1"></i> Goal created</div>`;
            } else if (cardStatus === 'cancelled') {
                actionsHtml = `<div class="pg-card-status-line status-cancelled"><i class="bi bi-x-circle me-1"></i> Cancelled</div>`;
            } else {
                actionsHtml = `
                    <div class="pg-price-card-actions">
                        <button type="button" class="btn btn-success pg-touch-btn pg-btn-main pg-btn-confirm-price-goal" data-name="${safeName}" data-price="${priceLookup.approxPrice}">
                            <i class="bi bi-check-lg me-1"></i> Set as goal
                        </button>
                        <div class="pg-price-card-secondary-row">
                            <button type="button" class="btn btn-outline-secondary pg-touch-btn pg-btn-toggle-change-price">
                                <i class="bi bi-pencil me-1"></i> Change price
                            </button>
                            <button type="button" class="btn btn-outline-danger pg-touch-btn pg-btn-cancel-price-goal" data-name="${safeName}">
                                <i class="bi bi-x-lg me-1"></i> Cancel
                            </button>
                        </div>
                    </div>
                    <div class="pg-change-price-container d-none mt-2">
                        <div class="small fw-semibold mb-1 text-dark">Enter your target price (₹):</div>
                        <div class="input-group input-group-sm mb-2">
                            <span class="input-group-text">₹</span>
                            <input type="number" inputmode="decimal" class="form-control pg-input-custom-price" value="${priceLookup.approxPrice}" min="1" max="100000000" />
                        </div>
                        <div class="pg-change-price-buttons">
                            <button type="button" class="btn btn-primary pg-touch-btn pg-btn-submit-custom-price" data-name="${safeName}">
                                <i class="bi bi-check2 me-1"></i> OK
                            </button>
                            <button type="button" class="btn btn-outline-secondary pg-touch-btn pg-btn-close-custom-price">
                                Cancel
                            </button>
                        </div>
                    </div>
                `;
            }

            priceLookupHtml = `
                <div class="pg-price-card mt-2">
                    <div class="pg-price-card-title">${safeName}</div>
                    <div class="pg-price-row">
                        <span class="pg-price-approx">approx.</span>
                        <span class="pg-price-amount">₹${priceFormatted}</span>
                        <span class="${isLivePrice ? 'pg-badge-live' : 'pg-badge-estimate'}">
                            <i class="bi ${isLivePrice ? 'bi-check-circle-fill' : 'bi-stars'} me-1"></i>
                            ${isLivePrice ? 'Live price' : 'AI estimate'}
                        </span>
                    </div>
                    ${safeVariant ? `<div class="pg-price-card-variant">${safeVariant}</div>` : ''}
                    <div class="pg-price-card-meta">
                        ${sourceLinkHtml}
                        <div class="pg-price-note">Prices change, please check the store page.</div>
                    </div>
                    ${actionsHtml}
                </div>
            `;
        }

        const safeContent = escapeHtml(content).replace(/\n/g, '<br/>');

        msgDiv.innerHTML = `
            <div class="pg-bubble">
                <div>${safeContent}</div>
                ${recordBadgeHtml}
                ${suggestionsHtml}
                ${priceLookupHtml}
            </div>
            <div class="pg-msg-time">${time || getCurrentTime()}</div>
        `;

        messagesContainer.appendChild(msgDiv);
        smartScrollToBottom();
        return msgDiv;
    }

    // Creates an individual pending typing bubble for a message
    function createPendingBubble(msgId) {
        const pendingDiv = document.createElement('div');
        pendingDiv.id = `pg-pending-${msgId}`;
        pendingDiv.className = 'pg-chat-msg bot';
        pendingDiv.innerHTML = `
            <div class="pg-bubble">
                <div class="pg-typing-indicator">
                    <span class="pg-typing-dot"></span>
                    <span class="pg-typing-dot"></span>
                    <span class="pg-typing-dot"></span>
                </div>
                <div class="pg-pending-meta">
                    <span>Thinking...</span> &bull;
                    <button type="button" class="pg-btn-cancel-pending" data-cancel-id="${msgId}">Cancel</button>
                </div>
            </div>
            <div class="pg-msg-time">${getCurrentTime()}</div>
        `;
        messagesContainer.appendChild(pendingDiv);
        smartScrollToBottom();
        return pendingDiv;
    }

    function handleRequestTimeout(msgId, userText) {
        const item = activeRequests.get(msgId);
        if (!item) return;
        activeRequests.delete(msgId);

        const pendingEl = item.pendingEl || document.getElementById(`pg-pending-${msgId}`);
        if (pendingEl) {
            const botTime = getCurrentTime();
            pendingEl.innerHTML = `
                <div class="pg-bubble">
                    <div class="text-warning-emphasis"><i class="bi bi-clock-history me-1"></i> This is taking too long. Please try again.</div>
                    <button type="button" class="btn btn-outline-primary pg-btn-retry" data-retry-text="${escapeHtml(userText)}">
                        <i class="bi bi-arrow-clockwise me-1"></i> Retry
                    </button>
                </div>
                <div class="pg-msg-time">${botTime}</div>
            `;
            smartScrollToBottom();
        }
    }

    function cancelRequest(msgId) {
        const item = activeRequests.get(msgId);
        if (!item) return;

        clearTimeout(item.timerId);
        if (item.controller) {
            item.controller.abort('user_cancelled');
        }
        activeRequests.delete(msgId);

        const pendingEl = item.pendingEl || document.getElementById(`pg-pending-${msgId}`);
        if (pendingEl) {
            const botTime = getCurrentTime();
            pendingEl.innerHTML = `
                <div class="pg-bubble">
                    <div class="text-muted"><i class="bi bi-x-circle me-1"></i> Request cancelled.</div>
                </div>
                <div class="pg-msg-time">${botTime}</div>
            `;
            smartScrollToBottom();
        }
    }

    async function sendMessage(text) {
        ensureSendButtonClean();
        const userText = text?.trim();
        if (!userText) return;

        const msgId = 'pg-msg-' + Date.now() + '-' + Math.random().toString(36).slice(2, 7);
        const nowTime = getCurrentTime();

        // 1. Render user message
        appendMessageBubble('user', userText, nowTime);
        history.push({ role: 'user', content: userText, time: nowTime });
        saveHistory();

        // 2. Clear input immediately; do NOT disable input or send button
        if (input) input.value = '';

        // 3. Render distinct pending bot bubble with typing indicator and cancel link
        const pendingEl = createPendingBubble(msgId);

        // 4. Extract last 3-4 completed messages as context (exclude pending)
        const recentHistory = history
            .filter(m => m.role === 'user' || m.role === 'model')
            .slice(-5, -1)
            .map(m => ({ role: m.role, content: m.content }));

        // 5. Setup AbortController and 30s client-side timeout
        const abortController = new AbortController();
        const timerId = setTimeout(() => {
            abortController.abort('timeout');
            handleRequestTimeout(msgId, userText);
        }, 30000);

        activeRequests.set(msgId, {
            controller: abortController,
            timerId: timerId,
            userText: userText,
            pendingEl: pendingEl
        });

        try {
            const response = await fetch('/api/chat', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify({
                    message: userText,
                    history: recentHistory
                }),
                signal: abortController.signal
            });

            // If request already timed out or cancelled, ignore
            if (!activeRequests.has(msgId)) return;
            clearTimeout(timerId);
            activeRequests.delete(msgId);

            // Remove pending bubble before replacing
            if (pendingEl) pendingEl.remove();

            if (!response.ok) {
                let errReply = 'Sorry, there was an issue connecting to the AI assistant.';
                try {
                    const errData = await response.json();
                    if (errData && errData.reply) {
                        errReply = errData.reply;
                    }
                } catch (_) {}

                const botTime = getCurrentTime();
                appendMessageBubble('bot', errReply, botTime, false);
                history.push({ role: 'model', content: errReply, time: botTime });
                saveHistory();
                return;
            }

            const data = await response.json();
            const botReply = data.reply || "Done!";
            const botTime = getCurrentTime();

            appendMessageBubble(
                'bot',
                botReply,
                botTime,
                data.saved,
                data.record,
                data.suggestions,
                data.isLive,
                data.priceLookup
            );

            history.push({
                role: 'model',
                content: botReply,
                time: botTime,
                saved: data.saved,
                record: data.record,
                suggestions: data.suggestions,
                isLive: data.isLive,
                priceLookup: data.priceLookup
            });
            saveHistory();

            if (data.saved === true) {
                handlePostSaveRefresh(data.record);
            }

        } catch (error) {
            // If request already handled by timeout or cancel, ignore
            if (!activeRequests.has(msgId)) return;
            clearTimeout(timerId);
            activeRequests.delete(msgId);

            if (pendingEl) pendingEl.remove();

            if (error.name === 'AbortError') {
                return; // Handled by cancel or timeout handlers
            }

            console.error('Chat error:', error);
            const botTime = getCurrentTime();
            const errReply = 'Network error: Unable to contact PocketGoal AI. Please try again.';
            appendMessageBubble('bot', errReply, botTime, false);
            history.push({ role: 'model', content: errReply, time: botTime });
            saveHistory();
        } finally {
            ensureSendButtonClean();
            if (input) input.focus();
        }
    }

    function handlePostSaveRefresh(record) {
        const path = window.location.pathname.toLowerCase();

        const isExpensesView = path.includes('/expenses');
        const isGoalsView = path.includes('/savinggoals');
        const isDashboardView = path.includes('/dashboard') || path === '/' || path === '';

        if (isExpensesView || isGoalsView || isDashboardView) {
            sessionStorage.setItem(STORAGE_KEY_IS_OPEN, 'true');
            setTimeout(() => {
                window.location.reload();
            }, 1200);
        }
    }

    async function submitGoalCreation(name, price, cardContainer, actionBtn) {
        if (!name || isNaN(price) || price <= 0) return;

        let originalHtml = '';
        if (actionBtn) {
            originalHtml = actionBtn.innerHTML;
            actionBtn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span> Saving...';
        }

        // Disable only buttons inside this specific cardContainer
        if (cardContainer) {
            cardContainer.querySelectorAll('button').forEach(b => b.disabled = true);
        }

        let created = false;
        try {
            const response = await fetch('/api/chat/create-goal-from-suggestion', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/json',
                    'Accept': 'application/json'
                },
                body: JSON.stringify({ name: name, price: price })
            });

            if (response.ok) {
                created = true;
                const data = await response.json();
                const priceFormatted = Number(price).toLocaleString('en-IN');

                // Replace buttons on the card with status line
                if (cardContainer) {
                    const actionsEl = cardContainer.querySelector('.pg-price-card-actions') || cardContainer.querySelector('.pg-suggestion-actions');
                    const changeEl = cardContainer.querySelector('.pg-change-price-container');
                    if (changeEl) changeEl.remove();

                    if (actionsEl) {
                        actionsEl.outerHTML = `<div class="pg-card-status-line status-success"><i class="bi bi-check-circle-fill me-1"></i> Goal created: ${escapeHtml(name)} (₹${priceFormatted})</div>`;
                    }
                }

                const botTime = getCurrentTime();
                const confirmMsg = data.message || `Set a goal for '${name}' at ₹${priceFormatted}! 🎯`;
                appendMessageBubble('bot', confirmMsg, botTime, true, { type: 'goal', name: name, targetAmount: price });
                history.push({
                    role: 'model',
                    content: confirmMsg,
                    time: botTime,
                    saved: true,
                    record: { type: 'goal', name: name, targetAmount: price }
                });
                saveHistory();

                handlePostSaveRefresh({ type: 'goal' });
            } else {
                const errData = await response.json().catch(() => ({}));
                const errMsg = errData.message || 'Failed to create goal.';
                if (window.PG && window.PG.alert) {
                    await PG.alert({ title: 'Goal Error', message: errMsg, variant: 'danger' });
                } else {
                    console.error('Goal Error:', errMsg);
                }
            }
        } catch (err) {
            console.error('Create goal error:', err);
            const netErr = 'Network error while creating goal. Please try again.';
            if (window.PG && window.PG.alert) {
                await PG.alert({ title: 'Network Error', message: netErr, variant: 'danger' });
            } else {
                console.error('Network Error:', netErr);
            }
        } finally {
            // If goal creation failed, re-enable card buttons and restore action button HTML
            if (!created && cardContainer) {
                cardContainer.querySelectorAll('button').forEach(b => b.disabled = false);
                if (actionBtn && originalHtml) {
                    actionBtn.innerHTML = originalHtml;
                }
            }
            ensureSendButtonClean();
            if (input) input.focus();
        }
    }

    // Event delegation for card actions & interactions
    messagesContainer?.addEventListener('click', async (e) => {
        // 0. Cancel pending message
        const cancelPendingBtn = e.target.closest('.pg-btn-cancel-pending');
        if (cancelPendingBtn) {
            const cancelId = cancelPendingBtn.getAttribute('data-cancel-id');
            if (cancelId) {
                cancelRequest(cancelId);
            }
            return;
        }

        // 0.1 Retry message on client timeout
        const retryBtn = e.target.closest('.pg-btn-retry');
        if (retryBtn) {
            const retryText = retryBtn.getAttribute('data-retry-text');
            if (retryText) {
                sendMessage(retryText);
            }
            return;
        }

        // 1. Suggestion Card "Set as goal"
        const suggestionGoalBtn = e.target.closest('.pg-btn-set-goal');
        if (suggestionGoalBtn && !suggestionGoalBtn.disabled) {
            const name = suggestionGoalBtn.getAttribute('data-name');
            const price = parseFloat(suggestionGoalBtn.getAttribute('data-price') || '0');
            if (!name || isNaN(price) || price <= 0) return;

            const card = suggestionGoalBtn.closest('.pg-suggestion-card');
            await submitGoalCreation(name, price, card, suggestionGoalBtn);
            return;
        }

        // 2. Price Lookup Card "Set as goal"
        const confirmPriceGoalBtn = e.target.closest('.pg-btn-confirm-price-goal');
        if (confirmPriceGoalBtn && !confirmPriceGoalBtn.disabled) {
            const name = confirmPriceGoalBtn.getAttribute('data-name');
            const price = parseFloat(confirmPriceGoalBtn.getAttribute('data-price') || '0');
            if (!name || isNaN(price) || price <= 0) return;

            const card = confirmPriceGoalBtn.closest('.pg-price-card');
            await submitGoalCreation(name, price, card, confirmPriceGoalBtn);
            return;
        }

        // 3. Price Lookup Card "Change price" toggle
        const toggleChangeBtn = e.target.closest('.pg-btn-toggle-change-price');
        if (toggleChangeBtn && !toggleChangeBtn.disabled) {
            const card = toggleChangeBtn.closest('.pg-price-card');
            const box = card?.querySelector('.pg-change-price-container');
            if (box) {
                box.classList.remove('d-none');
                const inp = box.querySelector('.pg-input-custom-price');
                setTimeout(() => inp?.focus(), 50);
                smartScrollToBottom();
            }
            return;
        }

        // 4. Price Lookup Card close custom price box
        const closeChangeBtn = e.target.closest('.pg-btn-close-custom-price');
        if (closeChangeBtn) {
            const box = closeChangeBtn.closest('.pg-change-price-container');
            box?.classList.add('d-none');
            return;
        }

        // 5. Price Lookup Card submit custom price
        const submitCustomBtn = e.target.closest('.pg-btn-submit-custom-price');
        if (submitCustomBtn && !submitCustomBtn.disabled) {
            const card = submitCustomBtn.closest('.pg-price-card');
            const name = submitCustomBtn.getAttribute('data-name');
            const inputEl = card?.querySelector('.pg-input-custom-price');
            const customPrice = parseFloat(inputEl?.value || '0');

            if (!name || isNaN(customPrice) || customPrice <= 0) {
                if (window.PG && window.PG.alert) {
                    await PG.alert({ title: 'Invalid Price', message: 'Please enter a valid target price greater than ₹0.', variant: 'danger' });
                } else {
                    console.error('Invalid Price');
                }
                return;
            }

            await submitGoalCreation(name, customPrice, card, submitCustomBtn);
            return;
        }

        // 6. Price Lookup Card Cancel
        const cancelPriceGoalBtn = e.target.closest('.pg-btn-cancel-price-goal');
        if (cancelPriceGoalBtn && !cancelPriceGoalBtn.disabled) {
            const name = cancelPriceGoalBtn.getAttribute('data-name');
            const card = cancelPriceGoalBtn.closest('.pg-price-card');
            if (card) {
                const actionsEl = card.querySelector('.pg-price-card-actions');
                const changeEl = card.querySelector('.pg-change-price-container');
                if (changeEl) changeEl.remove();
                if (actionsEl) {
                    actionsEl.outerHTML = `<div class="pg-card-status-line status-cancelled"><i class="bi bi-x-circle me-1"></i> Cancelled</div>`;
                }
            }
            const botTime = getCurrentTime();
            const cancelMsg = `Cancelled setting goal for '${name}'.`;
            appendMessageBubble('bot', cancelMsg, botTime);
            history.push({ role: 'model', content: cancelMsg, time: botTime });
            saveHistory();
            ensureSendButtonClean();
            if (input) input.focus();
            return;
        }
    });

    // Event listeners
    toggleBtn?.addEventListener('click', () => toggleChat());
    closeBtn?.addEventListener('click', () => toggleChat(false));

    clearBtn?.addEventListener('click', async () => {
        let confirmed = false;
        if (window.PG && window.PG.confirm) {
            confirmed = await PG.confirm({
                title: 'Clear Conversation',
                message: 'Are you sure you want to clear the chat conversation history?',
                confirmText: 'Clear Chat',
                cancelText: 'Cancel',
                variant: 'danger'
            });
        } else {
            confirmed = true;
        }

        if (confirmed) {
            history = [];
            sessionStorage.removeItem(STORAGE_KEY_HISTORY);
            renderWelcomeMessage();
        }
    });

    form?.addEventListener('submit', (e) => {
        e.preventDefault();
        e.stopPropagation();
        ensureSendButtonClean();
        sendMessage(input?.value);
    });

    sendBtn?.addEventListener('click', (e) => {
        e.preventDefault();
        e.stopPropagation();
        ensureSendButtonClean();
        sendMessage(input?.value);
    });

    input?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
            e.stopPropagation();
            ensureSendButtonClean();
            sendMessage(input.value);
        }
    });

    suggestionsContainer?.addEventListener('click', (e) => {
        const chip = e.target.closest('.pg-chip');
        if (chip) {
            const prompt = chip.getAttribute('data-prompt');
            if (prompt) {
                sendMessage(prompt);
            }
        }
    });

    ensureSendButtonClean();
    loadHistory();

    const wasOpen = sessionStorage.getItem(STORAGE_KEY_IS_OPEN) === 'true';
    if (wasOpen) {
        toggleChat(true);
    }
})();
