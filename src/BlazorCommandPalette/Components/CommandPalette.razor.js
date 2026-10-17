// Collocated JS module for the CommandPalette component.
// Handles: native <dialog> open/close, focus management, global hotkeys (incl. chord sequences),
// in-dialog navigation keys (with preventDefault), backdrop clicks, scroll-into-view, platform detection.

const NAV_KEYS = new Set(['ArrowDown', 'ArrowUp', 'Home', 'End', 'PageDown', 'PageUp', 'Enter', 'Tab', 'Escape']);
const MOD_KEYS = new Set(['Control', 'Shift', 'Alt', 'Meta', 'OS']);

function detectMac() {
  const uaData = navigator.userAgentData;
  if (uaData && uaData.platform) {
    return /mac|iphone|ipad|ipod/i.test(uaData.platform);
  }
  return /mac|iphone|ipad|ipod/i.test(navigator.platform || navigator.userAgent || '');
}

function isEditable(el) {
  if (!el) return false;
  const tag = el.tagName;
  if (tag === 'INPUT' || tag === 'TEXTAREA' || tag === 'SELECT') return true;
  return el.isContentEditable === true;
}

/** Normalize a KeyboardEvent into chord strings (both the literal and the 'mod' variant). */
function chordsFor(e, isMac) {
  let key = e.key;
  if (!key) return [];
  if (MOD_KEYS.has(key)) return [];
  key = key.length === 1 ? key.toLowerCase() : key.toLowerCase();
  if (key === ' ') key = ' ';

  const parts = [];
  if (e.ctrlKey) parts.push('ctrl');
  if (e.altKey) parts.push('alt');
  if (e.shiftKey) parts.push('shift');
  if (e.metaKey) parts.push('meta');

  const literal = [...parts, key].join('+');
  const result = [literal];

  // 'mod' variant: on mac Meta => Mod, elsewhere Ctrl => Mod.
  const modPressed = isMac ? e.metaKey : e.ctrlKey;
  if (modPressed) {
    const modParts = ['mod'];
    if (isMac && e.ctrlKey) modParts.push('ctrl');
    if (e.altKey) modParts.push('alt');
    if (e.shiftKey) modParts.push('shift');
    if (!isMac && e.metaKey) modParts.push('meta');
    result.push([...modParts, key].join('+'));
  }

  // Shifted single characters: allow matching "shift+?" written as "?" and vice versa.
  if (e.shiftKey && key.length === 1 && !e.ctrlKey && !e.metaKey && !e.altKey) {
    result.push(key);
  }

  return result;
}

export function init(dialog, dotnet, options) {
  const isMac = detectMac();
  let toggleShortcuts = new Set(options.toggleShortcuts || []);
  let commandShortcuts = new Set(options.commandShortcuts || []);
  const suppressInEditable = options.suppressInEditable !== false;
  const sequenceTimeout = options.sequenceTimeout || 1000;
  let vimBindings = options.vimBindings === true;

  let buffer = [];
  let bufferTimer = null;
  let previouslyFocused = null;
  let inputEl = null;
  let disposed = false;

  const allShortcuts = () => [...toggleShortcuts, ...commandShortcuts];

  function isReservedInside(e) {
    const lower = e.key.length === 1 ? e.key.toLowerCase() : e.key;
    const plainCtrl = e.ctrlKey && !e.shiftKey && !e.altKey && !e.metaKey;
    if (plainCtrl && (lower === 'n' || lower === 'p')) return true;
    if (vimBindings && plainCtrl && (lower === 'j' || lower === 'k')) return true;
    if (lower === 'p' && e.shiftKey && !e.altKey && (isMac ? e.metaKey : e.ctrlKey)) return true;
    return false;
  }

  function resetBuffer() {
    buffer = [];
    if (bufferTimer) {
      clearTimeout(bufferTimer);
      bufferTimer = null;
    }
  }

  /** Try to match the buffer (array of alternative-sets) against registered sequences. */
  function matchBuffer() {
    const sequences = allShortcuts();
    let prefixPossible = false;
    for (const seq of sequences) {
      const chords = seq.split(' ');
      if (chords.length > buffer.length) {
        // Could still be a prefix.
        let ok = true;
        for (let i = 0; i < buffer.length; i++) {
          if (!buffer[i].includes(chords[i])) { ok = false; break; }
        }
        if (ok) prefixPossible = true;
        continue;
      }
      // Compare tail of buffer.
      const offset = buffer.length - chords.length;
      let ok = true;
      for (let i = 0; i < chords.length; i++) {
        if (!buffer[offset + i].includes(chords[i])) { ok = false; break; }
      }
      if (ok) {
        return { matched: seq, prefixPossible };
      }
    }
    return { matched: null, prefixPossible };
  }

  function onDocumentKeyDown(e) {
    if (disposed || e.defaultPrevented || e.isComposing) return;
    const alts = chordsFor(e, isMac);
    if (alts.length === 0) return;

    const open = dialog.open;
    const target = e.target;
    const inEditable = isEditable(target);

    // Inside the open palette, only toggle shortcuts are considered globally.
    if (open) {
      // In-palette chords (Ctrl+N/P, Vim Ctrl+J/K, Mod+Shift+P pin) win over a toggle bound to the same chord.
      if (isReservedInside(e)) {
        return;
      }
      for (const t of toggleShortcuts) {
        if (!t.includes(' ') && alts.includes(t)) {
          e.preventDefault();
          dotnet.invokeMethodAsync('OnToggleShortcut');
          return;
        }
      }
      return;
    }

    // Plain (unmodified) keys never fire from editable elements; modified ones may.
    const hasModifier = e.ctrlKey || e.metaKey || e.altKey;
    if (inEditable && suppressInEditable && !hasModifier) {
      // Allow toggle shortcuts with modifiers only.
      resetBuffer();
      return;
    }

    buffer.push(alts);
    if (bufferTimer) clearTimeout(bufferTimer);
    bufferTimer = setTimeout(resetBuffer, sequenceTimeout);

    const { matched, prefixPossible } = matchBuffer();
    if (matched) {
      e.preventDefault();
      e.stopPropagation();
      resetBuffer();
      if (toggleShortcuts.has(matched)) {
        dotnet.invokeMethodAsync('OnToggleShortcut');
      } else {
        dotnet.invokeMethodAsync('OnGlobalShortcut', matched);
      }
      return;
    }
    if (!prefixPossible) {
      resetBuffer();
    }
  }

  function onDialogKeyDown(e) {
    if (disposed || e.isComposing) return;
    const key = e.key;
    const ctrl = e.ctrlKey, shift = e.shiftKey, alt = e.altKey, meta = e.metaKey;
    const lower = key.length === 1 ? key.toLowerCase() : key;

    let handled = false;
    if (NAV_KEYS.has(key)) {
      handled = true;
    } else if (ctrl && !shift && !alt && !meta && (lower === 'n' || lower === 'p')) {
      handled = true;
    } else if (vimBindings && ctrl && !shift && !alt && !meta && (lower === 'j' || lower === 'k')) {
      handled = true;
    } else if (key === 'Backspace' && inputEl && inputEl.value.length === 0) {
      handled = true;
    } else if ((key === 'ArrowLeft' || key === 'ArrowRight') && inputEl && inputEl.value.length === 0) {
      handled = true;
    } else if (key === ' ' && inputEl && inputEl.value.length === 0 && dialog.dataset.multiselect === 'true') {
      handled = true;
    } else if (key === 'Delete' && shift) {
      handled = true;
    } else if (lower === 'p' && shift && (isMac ? meta : ctrl)) {
      handled = true;
    }

    if (!handled) return;
    e.preventDefault();
    e.stopPropagation();
    dotnet.invokeMethodAsync('OnKeyDown', key, ctrl, shift, alt, meta);
  }

  function onCancel(e) {
    // We handle Escape ourselves (page back vs close).
    e.preventDefault();
  }

  function onDialogClick(e) {
    if (e.target === dialog) {
      dotnet.invokeMethodAsync('OnBackdropClick');
    }
  }

  function onPointerMove() {
    dotnet.invokeMethodAsync('OnPointerMoved');
  }

  document.addEventListener('keydown', onDocumentKeyDown, true);
  dialog.addEventListener('keydown', onDialogKeyDown);
  dialog.addEventListener('cancel', onCancel);
  dialog.addEventListener('click', onDialogClick);
  dialog.addEventListener('pointermove', onPointerMove, { passive: true });

  return {
    isMac() {
      return isMac;
    },

    open(input) {
      inputEl = input || null;
      previouslyFocused = document.activeElement;
      if (!dialog.open) {
        try {
          dialog.showModal();
        } catch {
          dialog.setAttribute('open', '');
        }
      }
      document.documentElement.classList.add('cp-scroll-lock');
      if (inputEl) {
        inputEl.focus({ preventScroll: true });
        inputEl.select();
      }
    },

    close() {
      if (dialog.open) {
        dialog.close();
      }
      document.documentElement.classList.remove('cp-scroll-lock');
      if (previouslyFocused && typeof previouslyFocused.focus === 'function') {
        try { previouslyFocused.focus({ preventScroll: true }); } catch { /* ignore */ }
      }
      previouslyFocused = null;
    },

    focusInput(input) {
      inputEl = input || inputEl;
      if (inputEl) {
        inputEl.focus({ preventScroll: true });
        const len = inputEl.value.length;
        try { inputEl.setSelectionRange(len, len); } catch { /* ignore */ }
      }
    },

    scrollIntoView(list, id) {
      const el = document.getElementById(id);
      if (!el) return;
      // Keep a little headroom so the group heading above the first item stays visible.
      const prev = el.previousElementSibling;
      const anchor = prev && prev.getAttribute('role') === 'presentation' ? prev : el;
      if (anchor !== el && list) {
        const r = list.getBoundingClientRect();
        const a = anchor.getBoundingClientRect();
        if (a.top < r.top) {
          anchor.scrollIntoView({ block: 'nearest' });
          return;
        }
      }
      el.scrollIntoView({ block: 'nearest' });
    },

    setCommandShortcuts(list) {
      commandShortcuts = new Set(list || []);
    },

    setToggleShortcuts(list) {
      toggleShortcuts = new Set(list || []);
    },

    setVimBindings(enabled) {
      vimBindings = enabled === true;
    },

    /** Resolves once the browser has had a chance to paint (or after a timeout, when frames are throttled). */
    afterPaint() {
      return new Promise(resolve => {
        const fallback = setTimeout(resolve, 100);
        requestAnimationFrame(() => setTimeout(() => {
          clearTimeout(fallback);
          resolve();
        }, 0));
      });
    },

    openInNewTab(url) {
      window.open(url, '_blank', 'noopener,noreferrer');
    },

    dispose() {
      disposed = true;
      document.removeEventListener('keydown', onDocumentKeyDown, true);
      dialog.removeEventListener('keydown', onDialogKeyDown);
      dialog.removeEventListener('cancel', onCancel);
      dialog.removeEventListener('click', onDialogClick);
      dialog.removeEventListener('pointermove', onPointerMove);
      document.documentElement.classList.remove('cp-scroll-lock');
      resetBuffer();
    },
  };
}
