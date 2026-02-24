export function setTheme(theme) {
    document.documentElement.setAttribute('data-healthie-theme', theme);
}

export function isTextOverflowing(element) {
    if (!element) return false;
    return element.scrollHeight > element.clientHeight;
}
