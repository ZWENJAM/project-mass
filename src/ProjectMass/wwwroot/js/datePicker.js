export function open(input) {
    try {
        if (typeof input.showPicker === 'function') { input.showPicker(); return true; }
    } catch { /* Fall back to an editable date input after the user clicks. */ }
    return false;
}
