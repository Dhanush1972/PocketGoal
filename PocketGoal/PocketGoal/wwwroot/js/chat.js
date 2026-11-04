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
    let isWaitingForResponse = false;

    // Helper: escape HTML to prevent XSS
    function escapeHtml(text) {
        if (!text) return '';
        const div = document.createElement('div');
        div.textContent = text;
        return div.innerHTML;
    }

    // Helper: get current time formatted
    function getCurrentTime() {
        const d = new Date();
        return d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
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
            scrollToBottom();
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
                        <li>"I spent 1200 on groceries yesterday"</li>
                        <li>"Suggest a laptop under 60000"</li>
                        <li>"Add 5000 to my savings"</li>
                    </ul>
                </div>
                <div class="pg-msg-time">${getCurrentTime()}</div>
            </div>
        `;
        messagesContainer.insertAdjacentHTML('beforeend', welcomeHtml);
        scrollToBottom();
    }

    function renderAllMessages() {
        messagesContainer.innerHTML = '';
        history.forEach(item => {
            appendMessageBubble(item.role, item.content, item.time, item.saved, item.record, item.suggestions, item.isLive);
        });
        scrollToBottom();
    }

    function appendMessageBubble(role, content, time, saved, record, suggestions, isLive) {
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
            suggestions.forEach(item => {
                const safeName = escapeHtml(item.name);
                const safeReason = escapeHtml(item.reason);
                const priceFormatted = Number(item.approxPrice || 0).toLocaleString('en-IN');

                let viewLinkHtml = '';
                if (item.sourceUrl && item.sourceUrl.startsWith('https://')) {
                    viewLinkHtml = `
                        <a href="${encodeURI(item.sourceUrl)}" target="_blank" rel="noopener noreferrer" class="btn btn-xs btn-outline-primary" title="View official source">
                            <i class="bi bi-box-arrow-up-right me-1"></i> View product
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
                    <div class="pg-suggestion-card">
                        <div class="pg-suggestion-title">${safeName}</div>
                        <div class="pg-suggestion-price">approx ₹${priceFormatted}</div>
                        <div class="pg-suggestion-reason">${safeReason}</div>
                        <div class="pg-suggestion-actions">
                            ${viewLinkHtml}
                            ${amazonSearchHtml}
                            ${flipkartSearchHtml}
                            <button type="button" class="btn btn-xs btn-primary pg-btn-set-goal" data-name="${safeName}" data-price="${item.approxPrice}">
                                <i class="bi bi-bullseye me-1"></i> Set as goal
                            </button>
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

        const safeContent = escapeHtml(content).replace(/\n/g, '<br/>');

        msgDiv.innerHTML = `
            <div class="pg-bubble">
                <div>${safeContent}</div>
                ${recordBadgeHtml}
                ${suggestionsHtml}
            </div>
            <div class="pg-msg-time">${time || getCurrentTime()}</div>
        `;

        messagesContainer.appendChild(msgDiv);
        scrollToBottom();
    }

    function showTypingIndicator() {
        removeTypingIndicator();
        const typingDiv = document.createElement('div');
        typingDiv.id = 'pgChatTyping';
        typingDiv.className = 'pg-chat-msg bot';
        typingDiv.innerHTML = `
            <div class="pg-bubble">
                <div class="pg-typing-indicator">
                    <span class="pg-typing-dot"></span>
                    <span class="pg-typing-dot"></span>
                    <span class="pg-typing-dot"></span>
                </div>
            </div>
        `;
        messagesContainer.appendChild(typingDiv);
        scrollToBottom();
    }

    function removeTypingIndicator() {
        const el = document.getElementById('pgChatTyping');
        if (el) el.remove();
    }

    function scrollToBottom() {
        setTimeout(() => {
            if (messagesContainer) {
                messagesContainer.scrollTop = messagesContainer.scrollHeight;
            }
        }, 30);
    }

    async function sendMessage(text) {
        const userText = text?.trim();
        if (!userText || isWaitingForResponse) return;

        const nowTime = getCurrentTime();
        appendMessageBubble('user', userText, nowTime);
        history.push({ role: 'user', content: userText, time: nowTime });
        saveHistory();

        if (input) input.value = '';

        const recentHistory = history
            .slice(-5, -1)
            .map(m => ({ role: m.role, content: m.content }));

        isWaitingForResponse = true;
        if (sendBtn) sendBtn.disabled = true;
        showTypingIndicator();

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
                })
            });

            removeTypingIndicator();

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

            appendMessageBubble('bot', botReply, botTime, data.saved, data.record, data.suggestions, data.isLive);
            history.push({
                role: 'model',
                content: botReply,
                time: botTime,
                saved: data.saved,
                record: data.record,
                suggestions: data.suggestions,
                isLive: data.isLive
            });
            saveHistory();

            if (data.saved === true) {
                handlePostSaveRefresh(data.record);
            }

        } catch (error) {
            console.error('Chat error:', error);
            removeTypingIndicator();
            const botTime = getCurrentTime();
            const errReply = 'Network error: Unable to contact PocketGoal AI. Please try again.';
            appendMessageBubble('bot', errReply, botTime, false);
            history.push({ role: 'model', content: errReply, time: botTime });
            saveHistory();
        } finally {
            isWaitingForResponse = false;
            if (sendBtn) sendBtn.disabled = false;
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

    // Handle "Set as goal" button clicks from suggestion cards
    messagesContainer?.addEventListener('click', async (e) => {
        const btn = e.target.closest('.pg-btn-set-goal');
        if (!btn || btn.disabled) return;

        const name = btn.getAttribute('data-name');
        const price = parseFloat(btn.getAttribute('data-price') || '0');

        if (!name || isNaN(price) || price <= 0) return;

        btn.disabled = true;
        const originalHtml = btn.innerHTML;
        btn.innerHTML = '<span class="spinner-border spinner-border-sm me-1" role="status" aria-hidden="true"></span> Saving...';

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
                const data = await response.json();
                btn.classList.remove('btn-primary');
                btn.classList.add('btn-success');
                btn.innerHTML = '<i class="bi bi-check2-circle me-1"></i> Goal Set!';
                btn.disabled = true;

                const botTime = getCurrentTime();
                const confirmMsg = data.message || `Set a goal for '${name}' at ₹${price.toLocaleString('en-IN')}! 🎯`;
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
                btn.disabled = false;
                btn.innerHTML = originalHtml;
                const errData = await response.json().catch(() => ({}));
                const errMsg = errData.message || 'Failed to create goal from suggestion.';
                if (window.PG && window.PG.alert) {
                    await PG.alert({ title: 'Goal Error', message: errMsg, variant: 'danger' });
                } else {
                    console.error('Goal Error:', errMsg);
                }
            }
        } catch (err) {
            console.error('Create goal error:', err);
            btn.disabled = false;
            btn.innerHTML = originalHtml;
            const netErr = 'Network error while creating goal. Please try again.';
            if (window.PG && window.PG.alert) {
                await PG.alert({ title: 'Network Error', message: netErr, variant: 'danger' });
            } else {
                console.error('Network Error:', netErr);
            }
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
        sendMessage(input?.value);
    });

    input?.addEventListener('keydown', (e) => {
        if (e.key === 'Enter' && !e.shiftKey) {
            e.preventDefault();
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

    loadHistory();

    const wasOpen = sessionStorage.getItem(STORAGE_KEY_IS_OPEN) === 'true';
    if (wasOpen) {
        toggleChat(true);
    }
})();
