// Triggers a browser download for text generated in .NET (backup JSON, CSV).
export function downloadText(fileName, content, mimeType) {
    const url = URL.createObjectURL(new Blob([content], { type: mimeType }));
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.appendChild(link);
    link.click();
    link.remove();
    setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// Opens the system share sheet with the backup attached (Gmail, Outlook, Mail …).
// Chrome only shares a fixed list of file types, so the backup goes out as .txt with JSON content.
// Returns "shared", "cancelled" or "unsupported".
export async function shareTextFile(fileName, content, title, text) {
    try {
        const file = new File([content], fileName, { type: 'text/plain' });
        if (!navigator.canShare || !navigator.canShare({ files: [file] })) return 'unsupported';
        await navigator.share({ files: [file], title, text });
        return 'shared';
    } catch (error) {
        return error && error.name === 'AbortError' ? 'cancelled' : 'unsupported';
    }
}

export function openMail(subject, body) {
    window.location.href = `mailto:?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`;
}

export async function copyText(text) {
    try { await navigator.clipboard.writeText(text); return true; }
    catch { return false; }
}
