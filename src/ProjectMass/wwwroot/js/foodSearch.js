export function isOnline() { return navigator.onLine; }
const scanners = new Map();
export async function startScanner(video, callback, id) {
    stopScanner(id);
    const state = { active: true, stream: null, frame: null };
    scanners.set(id, state);
    if (!('BarcodeDetector' in globalThis) || !navigator.mediaDevices?.getUserMedia) {
        stopScanner(id);
        return 'Die Kamera-Erkennung ist hier nicht verfügbar. Bitte Barcode manuell eingeben.';
    }
    try {
        const wanted = ['ean_13', 'ean_8', 'upc_a', 'upc_e'];
        const supported = await BarcodeDetector.getSupportedFormats();
        const formats = wanted.filter(format => supported.includes(format));
        if (!formats.length) { stopScanner(id); return 'Diese Barcode-Formate werden nicht unterstützt. Bitte manuell eingeben.'; }
        const detector = new BarcodeDetector({ formats });
        if (!state.active) return '';
        const stream = await navigator.mediaDevices.getUserMedia({ video: { facingMode: { ideal: 'environment' } }, audio: false });
        if (!state.active) { stream.getTracks().forEach(track => track.stop()); return ''; }
        state.stream = stream;
        video.srcObject = stream;
        await video.play();
        const scan = async () => {
            if (!state.active) return;
            try {
                const results = await detector.detect(video);
                if (!state.active) return;
                const result = results.find(item => /^\d{8,14}$/.test(item.rawValue));
                if (result) {
                    stopScanner(id);
                    await callback.invokeMethodAsync('Detected', result.rawValue);
                    return;
                }
            } catch { /* A frame may not yet be ready. */ }
            if (state.active) state.frame = requestAnimationFrame(scan);
        };
        state.frame = requestAnimationFrame(scan);
        return 'Kamera aktiv. Halte den Barcode in die Kamera.';
    } catch {
        stopScanner(id);
        return 'Die Kamera konnte nicht gestartet werden. Bitte Barcode manuell eingeben.';
    }
}
export function stopScanner(id) {
    const state = scanners.get(id);
    if (!state) return;
    state.active = false;
    if (state.frame !== null) cancelAnimationFrame(state.frame);
    state.stream?.getTracks().forEach(track => track.stop());
    scanners.delete(id);
}
