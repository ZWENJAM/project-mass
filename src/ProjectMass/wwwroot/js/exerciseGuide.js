export function open(dialog) {
    if (dialog.isConnected && !dialog.open) dialog.showModal();
}
