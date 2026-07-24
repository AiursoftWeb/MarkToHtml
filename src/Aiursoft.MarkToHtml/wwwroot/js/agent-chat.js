(function() {
    'use strict';

    var conversationId = null;
    var pollTimer = null;
    var lastMessageCount = 0;
    var token = '';
    var pollInterval = 1000;
    var renderedAdviceIds = [];

    // These will be set by the editor page via AgentChat.init()
    var _documentId = null;
    var _getDocumentContent = null; // callback: () => string
    var _setDocumentContent = null; // callback: (content: string) => void

    function loc(key, fallback) {
        var el = document.querySelector('#agent-loc-data span[data-key="' + key + '"]');
        return el ? el.innerText : (fallback || key);
    }

    function init(antiForgeryToken, documentId, getDocContent, setDocContent) {
        token = antiForgeryToken;
        _documentId = documentId;
        _getDocumentContent = getDocContent;
        _setDocumentContent = setDocContent;

        var widget = document.getElementById('agent-chat-widget');
        if (!widget) {
            console.debug('AgentChat: widget not found, skipping init');
            return;
        }

        var sendBtn = document.getElementById('agent-send-btn');
        var input = document.getElementById('agent-input');
        var newChatBtn = document.getElementById('agent-new-chat-btn');

        console.debug('AgentChat: init called, widget=', !!widget, 'sendBtn=', !!sendBtn,
            'input=', !!input, 'newChatBtn=', !!newChatBtn);

        if (sendBtn) {
            sendBtn.addEventListener('click', function() { sendMessage(); });
        }
        if (input) {
            input.addEventListener('keydown', function(ev) {
                if (ev.key === 'Enter' && !ev.shiftKey) {
                    ev.preventDefault();
                    sendMessage();
                }
            });
        }

        if (newChatBtn) {
            newChatBtn.addEventListener('click', function() { resetConversation(); });
        }
    }

    function sendMessage() {
        var input = document.getElementById('agent-input');
        if (!input) return;
        var message = input.value.trim();
        if (!message) return;

        // Require a saved document
        if (!_documentId || _documentId === '00000000-0000-0000-0000-000000000000') {
            appendMessage('assistant', loc('save-first', 'Please save the document first before using the AI assistant.'));
            return;
        }

        // Don't send while the agent is processing
        if (conversationId) {
            var statusEl = document.getElementById('agent-status-text');
            if (statusEl && statusEl.textContent === loc('thinking-status', 'Thinking...')) return;
        }

        input.value = '';

        var docContent = _getDocumentContent ? _getDocumentContent() : '';
        var body = {
            documentId: _documentId,
            message: message,
            fullDocumentContent: docContent
        };
        if (conversationId) {
            body.ConversationId = conversationId;
        }

        showThinking();

        fetch('/Agent/SendMessage', {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': token
            },
            body: JSON.stringify(body)
        })
        .then(function(r) { return r.json(); })
        .then(function(data) {
            if (data.Error) {
                hideThinking();
                appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.Error);
                return;
            }
            conversationId = data.ConversationId;
            pollStatus();
            startPolling();
        })
        .catch(function(err) {
            hideThinking();
            appendMessage('assistant', loc('network-error', 'Network error:') + ' ' + err.message);
        });
    }

    function startPolling() {
        if (pollTimer) clearInterval(pollTimer);
        pollTimer = setInterval(pollStatus, pollInterval);
    }

    function stopPolling() {
        if (pollTimer) { clearInterval(pollTimer); pollTimer = null; }
    }

    function pollStatus() {
        if (!conversationId) { stopPolling(); return; }

        fetch('/Agent/Status?conversationId=' + conversationId)
            .then(function(r) { return r.json(); })
            .then(function(data) {
                if (data.Error) { stopPolling(); return; }
                renderMessages(data);
                renderAdvice(data);
                updateState(data.State);

                // Apply updated document content
                if (data.UpdatedDocumentContent && _setDocumentContent) {
                    _setDocumentContent(data.UpdatedDocumentContent);
                }

                if (data.State === 'Error') {
                    stopPolling();
                    conversationId = null;
                    if (data.ErrorMessage) {
                        appendMessage('assistant', loc('error-prefix', 'Error:') + ' ' + data.ErrorMessage);
                    }
                } else if (data.State === 'Completed') {
                    stopPolling();
                }
            })
            .catch(function() { /* retry on next poll */ });
    }

    function renderMessages(data) {
        var container = document.getElementById('agent-messages');
        if (!container || !data.Messages) return;

        for (var i = lastMessageCount; i < data.Messages.length; i++) {
            var msg = data.Messages[i];

            if (msg.Role === 'assistant' && msg.ToolCalls && msg.ToolCalls.length > 0 && !msg.Content) {
                continue;
            }

            if (msg.Role === 'tool') {
                continue;
            }

            appendMessage(msg.Role, msg.Content);
        }
        lastMessageCount = data.Messages.length;
    }

    function renderAdvice(data) {
        var container = document.getElementById('agent-messages');
        if (!container) return;

        var pendingIds = (data.PendingAdvice || []).map(function(a) { return a.AdviceId; });

        // Remove cards whose advice is no longer pending
        var existingCards = document.querySelectorAll('.advice-card[data-conversation]');
        existingCards.forEach(function(card) {
            var adviceId = card.getAttribute('data-advice-id');
            if (pendingIds.indexOf(adviceId) === -1) {
                card.remove();
                renderedAdviceIds = renderedAdviceIds.filter(function(id) { return id !== adviceId; });
            }
        });

        if (!data.PendingAdvice || data.PendingAdvice.length === 0) return;

        data.PendingAdvice.forEach(function(advice) {
            if (renderedAdviceIds.indexOf(advice.AdviceId) !== -1) return;
            renderedAdviceIds.push(advice.AdviceId);

            var card = document.createElement('div');
            card.className = 'advice-card';
            card.setAttribute('data-conversation', data.ConversationId);
            card.setAttribute('data-advice-id', advice.AdviceId);

            var header = document.createElement('div');
            header.className = 'advice-header';
            header.textContent = loc('proposed-edit', 'Proposed Edit');
            card.appendChild(header);

            // Render diff hunks
            if (advice.DiffHunks && advice.DiffHunks.length > 0) {
                var diffContainer = document.createElement('div');
                diffContainer.className = 'advice-diff';

                advice.DiffHunks.forEach(function(hunk) {
                    var hunkDiv = document.createElement('div');
                    hunkDiv.className = 'diff-hunk';

                    var hunkHeader = document.createElement('div');
                    hunkHeader.className = 'diff-hunk-header';
                    hunkHeader.textContent = '@@ -' + hunk.OldStart + ',' + hunk.OldLines +
                        ' +' + hunk.NewStart + ',' + hunk.NewLines + ' @@';
                    hunkDiv.appendChild(hunkHeader);

                    hunk.Lines.forEach(function(line) {
                        var lineDiv = document.createElement('div');
                        if (line.startsWith('+')) {
                            lineDiv.className = 'diff-line diff-add';
                        } else if (line.startsWith('-')) {
                            lineDiv.className = 'diff-line diff-remove';
                        } else {
                            lineDiv.className = 'diff-line diff-context';
                        }
                        lineDiv.textContent = line;
                        hunkDiv.appendChild(lineDiv);
                    });

                    diffContainer.appendChild(hunkDiv);
                });

                card.appendChild(diffContainer);
            }

            // Actions
            var actions = document.createElement('div');
            actions.className = 'advice-actions';

            var approveBtn = document.createElement('button');
            approveBtn.className = 'btn btn-sm btn-success';
            approveBtn.textContent = loc('approve', 'Approve');
            approveBtn.addEventListener('click', function() {
                approveAdvice(advice.AdviceId, card);
            });

            var rejectBtn = document.createElement('button');
            rejectBtn.className = 'btn btn-sm btn-outline-danger';
            rejectBtn.textContent = loc('reject', 'Reject');
            rejectBtn.addEventListener('click', function() {
                rejectAdvice(advice.AdviceId, card);
            });

            actions.appendChild(approveBtn);
            actions.appendChild(rejectBtn);
            card.appendChild(actions);
            container.appendChild(card);
        });
    }

    function approveAdvice(adviceId, cardElement) {
        if (!conversationId) return;
        disableAdviceButtons(cardElement);

        fetch('/Agent/ApproveAdvice?conversationId=' + conversationId + '&adviceId=' + adviceId, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        })
        .then(function() {
            showResult(cardElement, true, loc('approved-executing', 'Approved - applying...'));
            startPolling();
        });
    }

    function rejectAdvice(adviceId, cardElement) {
        if (!conversationId) return;
        disableAdviceButtons(cardElement);

        fetch('/Agent/RejectAdvice?conversationId=' + conversationId + '&adviceId=' + adviceId, {
            method: 'POST',
            headers: { 'RequestVerificationToken': token }
        })
        .then(function() {
            showResult(cardElement, false, loc('rejected', 'Rejected'));
            startPolling();
        });
    }

    function disableAdviceButtons(cardElement) {
        var buttons = cardElement.querySelectorAll('button');
        buttons.forEach(function(b) { b.disabled = true; });
    }

    function showResult(cardElement, success, text) {
        var existing = cardElement.querySelector('.advice-result');
        if (existing) existing.remove();
        var result = document.createElement('div');
        result.className = 'advice-result ' + (success ? 'success' : 'failure');
        result.textContent = text;
        cardElement.appendChild(result);
    }

    function appendMessage(role, content) {
        if (!content) return;
        var container = document.getElementById('agent-messages');
        if (!container) return;

        var div = document.createElement('div');
        div.className = 'chat-message ' + role;
        div.textContent = content;
        container.appendChild(div);
        container.scrollTop = container.scrollHeight;
    }

    function showThinking() {
        var bar = document.getElementById('agent-thinking-bar');
        if (bar) bar.style.display = 'flex';
    }

    function hideThinking() {
        var bar = document.getElementById('agent-thinking-bar');
        if (bar) bar.style.display = 'none';
    }

    function updateState(state) {
        var statusEl = document.getElementById('agent-status-text');

        if (state === 'Error') {
            if (statusEl) statusEl.textContent = loc('error', 'Error');
            hideThinking();
        } else if (state === 'Completed') {
            if (statusEl) statusEl.textContent = loc('ready', 'Ready');
            hideThinking();
        } else if (state === 'AwaitingApproval') {
            if (statusEl) statusEl.textContent = loc('waiting-approval', 'Waiting for approval');
            hideThinking();
        } else if (state === 'Thinking') {
            if (statusEl) statusEl.textContent = loc('thinking-status', 'Thinking...');
        }
    }

    function resetConversation() {
        if (conversationId) {
            fetch('/Agent/Cancel?conversationId=' + conversationId, {
                method: 'POST',
                headers: { 'RequestVerificationToken': token }
            }).catch(function() {});
        }
        conversationId = null;
        lastMessageCount = 0;
        renderedAdviceIds = [];
        stopPolling();
        hideThinking();

        var container = document.getElementById('agent-messages');
        if (container) {
            container.innerHTML = '';
            var welcome = document.createElement('div');
            welcome.className = 'chat-message assistant';
            welcome.textContent = loc('welcome', 'Hi!') || "Hi! I'm your editing assistant.";
            container.appendChild(welcome);
        }

        var statusEl = document.getElementById('agent-status-text');
        if (statusEl) statusEl.textContent = loc('ready', 'Ready');
    }

    function escapeHtml(text) {
        var div = document.createElement('div');
        div.appendChild(document.createTextNode(text));
        return div.innerHTML;
    }

    function updateCallbacks(getDocContent, setDocContent) {
        _getDocumentContent = getDocContent;
        _setDocumentContent = setDocContent;
    }

    window.AgentChat = {
        init: init,
        updateCallbacks: updateCallbacks,
        toggleWidget: toggleWidget,
        sendMessage: sendMessage,
        resetConversation: resetConversation,
        handleInputKeydown: handleInputKeydown,
        approveAdvice: approveAdvice,
        rejectAdvice: rejectAdvice
    };

    function handleInputKeydown(ev) {
        if (ev.key === 'Enter' && !ev.shiftKey) {
            ev.preventDefault();
            sendMessage();
        }
    }

    function toggleWidget() {
        var widget = document.getElementById('agent-chat-widget');
        if (!widget) return;
        if (widget.style.display === 'none') {
            widget.style.display = '';
        } else {
            widget.style.display = 'none';
        }
    }
})();
