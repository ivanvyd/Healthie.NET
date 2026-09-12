export function setTheme(theme) {
    document.documentElement.setAttribute('data-healthie-theme', theme);
}

export function isTextOverflowing(element) {
    if (!element) return false;
    return element.scrollHeight > element.clientHeight;
}

const dialogState = new WeakMap();

const focusableSelector = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
].join(',');

function focusableElements(dialog) {
    return Array.from(dialog.querySelectorAll(focusableSelector))
        .filter(element => element.getClientRects().length > 0);
}

export function activateDialog(dialog) {
    if (!dialog || dialogState.has(dialog)) return;

    const returnFocus = document.activeElement instanceof HTMLElement
        ? document.activeElement
        : null;

    const trapFocus = event => {
        if (event.key !== 'Tab') return;

        const focusable = focusableElements(dialog);
        if (focusable.length === 0) {
            event.preventDefault();
            dialog.focus();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const movingBeforeFirst = event.shiftKey
            && (document.activeElement === first || !dialog.contains(document.activeElement));
        const movingAfterLast = !event.shiftKey
            && (document.activeElement === last || !dialog.contains(document.activeElement));

        if (movingBeforeFirst || movingAfterLast) {
            event.preventDefault();
            (movingBeforeFirst ? last : first).focus();
        }
    };

    dialogState.set(dialog, { returnFocus, trapFocus });
    dialog.addEventListener('keydown', trapFocus);

    const first = focusableElements(dialog)[0] ?? dialog;
    first.focus();
}

export function deactivateDialog(dialog) {
    const state = dialog ? dialogState.get(dialog) : null;
    if (!state) return;

    dialog.removeEventListener('keydown', state.trapFocus);
    dialogState.delete(dialog);

    if (state.returnFocus?.isConnected) {
        state.returnFocus.focus();
    }
}
