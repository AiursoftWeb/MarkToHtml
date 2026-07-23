/**
 * TtsPlayer — Chunked TTS playback with proper play/pause/resume.
 *
 * Usage:
 *   new TtsPlayer({
 *       button: document.getElementById('read-aloud-button'),
 *       voiceSelect: document.getElementById('voice-select'),
 *       getText: () => document.getElementById('printable-area')?.textContent?.trim() || '',
 *       labels: {
 *           play: '...',    // innerHTML for idle button
 *           pause: '...',   // innerHTML for playing button
 *           resume: '...'   // innerHTML for paused button
 *       },
 *       playingClass: 'btn-primary',
 *       idleClass: 'btn-white'
 *   });
 */
class TtsPlayer {
    constructor(options) {
        this.button = options.button;
        this.voiceSelect = options.voiceSelect;
        this.getText = options.getText;
        this.labels = options.labels;
        this.playingClass = options.playingClass || 'btn-primary';
        this.idleClass = options.idleClass || 'btn-white';

        // State machine: idle → playing → paused → playing → ... → idle
        this.state = 'idle';
        this.abortController = null;
        this.currentAudio = null;
        /** @type {string[]} */
        this.chunks = [];
        this.nextIdx = 0;
        /** @type {HTMLAudioElement|null} */
        this.preloaded = null;

        if (this.button) {
            this.button.addEventListener('click', () => this.toggle());
        }
        this._loadVoices();
    }

    // ---- public API ----

    toggle() {
        switch (this.state) {
            case 'idle': this.play(); break;
            case 'playing': this.pause(); break;
            case 'paused': this.resume(); break;
        }
    }

    async play() {
        var text = this.getText();
        if (!text) return;

        this.chunks = TtsPlayer.splitText(text);
        if (!this.chunks.length) return;

        this._setState('playing');
        this.nextIdx = 0;
        this.preloaded = null;
        this.abortController = new AbortController();

        await this._playNext();
    }

    pause() {
        this._setState('paused');
        if (this.currentAudio) {
            this.currentAudio.pause();
        }
    }

    resume() {
        this._setState('playing');
        var self = this;
        if (this.currentAudio && !this.currentAudio.ended && this.currentAudio.src) {
            this.currentAudio.play().catch(function () {
                // Audio element was reset externally; move to next chunk
                self._playNext();
            });
        } else {
            this._playNext();
        }
    }

    destroy() {
        if (this.abortController) {
            this.abortController.abort();
            this.abortController = null;
        }
        if (this.currentAudio) {
            this.currentAudio.pause();
            if (this.currentAudio.src) {
                URL.revokeObjectURL(this.currentAudio.src);
            }
            this.currentAudio.removeAttribute('src');
            this.currentAudio.load();
            this.currentAudio = null;
        }
        // Clean up any preloaded audio that hasn't started playing yet
        if (this.preloaded) {
            if (this.preloaded.src) {
                URL.revokeObjectURL(this.preloaded.src);
            }
            this.preloaded = null;
        }
        this.chunks = [];
        this._setState('idle');
    }

    // ---- internal ----

    async _loadVoices() {
        if (!this.voiceSelect) return;
        try {
            var response = await fetch('/tts/voices');
            if (!response.ok) {
                this.voiceSelect.classList.add('d-none');
                return;
            }
            var voices = await response.json();
            if (!voices || !voices.length) {
                this.voiceSelect.classList.add('d-none');
                return;
            }
            var currentValue = this.voiceSelect.value;
            this.voiceSelect.innerHTML = '';
            voices.forEach(function (v) {
                var opt = document.createElement('option');
                opt.value = v.name;
                opt.textContent = v.name;
                this.voiceSelect.appendChild(opt);
            }, this);
            if (currentValue && voices.some(function (v) { return v.name === currentValue; })) {
                this.voiceSelect.value = currentValue;
            }
            this.voiceSelect.classList.remove('d-none');
        } catch (e) {
            console.error('Failed to load TTS voices:', e);
            this.voiceSelect.classList.add('d-none');
        }
    }

    /**
     * Fetch audio for a single text chunk from the TTS API.
     * @param {string} chunkText
     * @returns {Promise<HTMLAudioElement>}
     */
    async _fetchTtsChunk(chunkText) {
        var formData = new FormData();
        formData.append('input', chunkText);
        var voice = this.voiceSelect ? this.voiceSelect.value : '';
        if (voice) formData.append('voice', voice);

        var response = await fetch('/tts/speech', {
            method: 'POST',
            body: formData,
            signal: this.abortController ? this.abortController.signal : undefined
        });

        if (!response.ok) {
            var err = await response.json().catch(function () { return {}; });
            throw new Error(err.error || response.statusText);
        }

        var blob = await response.blob();
        var url = URL.createObjectURL(blob);
        return new Audio(url);
    }

    /**
     * Preload the next chunk (fetch and return Audio).
     * @returns {Promise<HTMLAudioElement|null>}
     */
    async _preloadNext() {
        if (this.nextIdx >= this.chunks.length) return null;
        var idx = this.nextIdx++;
        try {
            return await this._fetchTtsChunk(this.chunks[idx]);
        } catch (e) {
            if (e.name === 'AbortError') return null;
            console.error('TTS chunk ' + idx + ' error:', e);
            return null;
        }
    }

    /**
     * Play the next preloaded chunk, then recurse.
     */
    async _playNext() {
        if (this.state !== 'playing') return;

        // Ensure we have a preloaded audio chunk
        if (!this.preloaded) {
            this.preloaded = await this._preloadNext();
        }

        if (!this.preloaded) {
            // All chunks consumed — done
            this.destroy();
            return;
        }

        var self = this;
        this.currentAudio = this.preloaded;
        var current = this.currentAudio;

        // Start preloading the next chunk in background
        this.preloaded = this._preloadNext();

        current.onended = function () {
            URL.revokeObjectURL(current.src);
            if (self.currentAudio === current) {
                self._playNext();
            }
        };

        current.onerror = function (e) {
            console.error('Audio playback error:', e);
            URL.revokeObjectURL(current.src);
            if (self.currentAudio === current) {
                if (!self.preloaded) {
                    self.destroy();
                } else {
                    self._playNext();
                }
            }
        };

        current.play();
    }

    _setState(newState) {
        this.state = newState;
        this._updateButton();
    }

    _updateButton() {
        if (!this.button) return;
        switch (this.state) {
            case 'idle':
                this.button.innerHTML = this.labels.play;
                this.button.classList.remove(this.playingClass);
                this.button.classList.add(this.idleClass);
                break;
            case 'playing':
                this.button.innerHTML = this.labels.pause;
                this.button.classList.remove(this.idleClass);
                this.button.classList.add(this.playingClass);
                break;
            case 'paused':
                this.button.innerHTML = this.labels.resume;
                this.button.classList.remove(this.playingClass);
                this.button.classList.add(this.idleClass);
                break;
        }
        this.button.disabled = false;
        if (window.lucide) lucide.createIcons();
    }

    // ---- static utilities ----

    static TTS_DELIMITERS = ['。', '！', '？', '；', '，', '.', '!', '?', ';', ',', '\n', '\r'];
    static TTS_MAX_CHUNK = 300;

    /**
     * Split text at natural boundaries for chunked TTS processing.
     * @param {string} text
     * @returns {string[]}
     */
    static splitText(text) {
        if (!text || !text.trim()) return [];
        text = text.trim();
        if (text.length <= TtsPlayer.TTS_MAX_CHUNK) return [text];

        var delimiters = TtsPlayer.TTS_DELIMITERS;
        var chunks = [];
        var remaining = text;

        while (remaining.length > 0) {
            if (remaining.length <= TtsPlayer.TTS_MAX_CHUNK) {
                chunks.push(remaining.trim());
                break;
            }

            var window = remaining.substring(0, TtsPlayer.TTS_MAX_CHUNK);
            var lastDelim = -1;
            for (var i = window.length - 1; i >= 0; i--) {
                if (delimiters.indexOf(window[i]) >= 0) {
                    lastDelim = i;
                    break;
                }
            }

            if (lastDelim >= 0) {
                chunks.push(remaining.substring(0, lastDelim + 1).trim());
                remaining = remaining.substring(lastDelim + 1);
            } else {
                // No delimiter found — hard split at word boundary or max
                var spaceIdx = window.lastIndexOf(' ');
                var splitAt = spaceIdx > TtsPlayer.TTS_MAX_CHUNK / 2 ? spaceIdx + 1 : TtsPlayer.TTS_MAX_CHUNK;
                chunks.push(remaining.substring(0, splitAt).trim());
                remaining = remaining.substring(splitAt);
            }
        }

        return chunks.filter(function (c) { return c.length > 0; });
    }
}
