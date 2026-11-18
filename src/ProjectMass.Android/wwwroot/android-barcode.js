// EAN and UPC decoder for Android WebView.
const leftOdd = ['0001101','0011001','0010011','0111101','0100011','0110001','0101111','0111011','0110111','0001011'];
const right = leftOdd.map(pattern => pattern.replace(/[01]/g, bit => bit === '0' ? '1' : '0'));
const leftEven = right.map(pattern => [...pattern].reverse().join(''));
const parity = ['LLLLLL','LLGLGG','LLGGLG','LLGGGL','LGLLGG','LGGLLG','LGGGLL','LGLGLG','LGLGGL','LGGLGL'];
const upcEParity = [0x38, 0x34, 0x32, 0x31, 0x2c, 0x26, 0x23, 0x2a, 0x29, 0x25];

function decode(bits, digits) {
    if (digits === 6) return decodeUpcE(bits);
    const half = digits === 13 ? 6 : 4;
    const center = 3 + 7 * half;
    if (!bits.startsWith('101') || bits.slice(center, center + 5) !== '01010' || !bits.endsWith('101')) return null;
    let result = '', scheme = '';
    for (let i = 0; i < half; i++) {
        const group = bits.slice(3 + i * 7, 10 + i * 7);
        let digit = leftOdd.indexOf(group);
        if (digit >= 0) scheme += 'L';
        else if (digits === 13 && (digit = leftEven.indexOf(group)) >= 0) scheme += 'G';
        else return null;
        result += digit;
    }
    if (digits === 13) {
        const lead = parity.indexOf(scheme);
        if (lead < 0) return null;
        result = lead + result;
    }
    for (let i = 0; i < half; i++) {
        const group = bits.slice(center + 5 + i * 7, center + 12 + i * 7);
        const digit = right.indexOf(group);
        if (digit < 0) return null;
        result += digit;
    }
    return hasCheckDigit(result) ? result : null;
}

function hasCheckDigit(result) {
    let sum = 0;
    for (let i = 0; i < result.length; i++)
        sum += Number(result[i]) * ((result.length - 1 - i) % 2 ? 3 : 1);
    return sum % 10 === 0;
}

function decodeUpcE(bits) {
    if (!bits.startsWith('101') || !bits.endsWith('010101')) return null;
    let body = '', mask = 0;
    for (let i = 0; i < 6; i++) {
        const group = bits.slice(3 + i * 7, 10 + i * 7);
        let digit = leftOdd.indexOf(group);
        if (digit < 0) {
            digit = leftEven.indexOf(group);
            if (digit < 0) return null;
            mask |= 1 << (5 - i);
        }
        body += digit;
    }
    const system = upcEParity.includes(mask) ? '0' : '1';
    const check = upcEParity.indexOf(system === '0' ? mask : mask ^ 0x3f);
    if (check < 0) return null;
    const last = body[5];
    let expanded;
    if (last <= '2') expanded = body.slice(0, 2) + last + '0000' + body.slice(2, 5);
    else if (last === '3') expanded = body.slice(0, 3) + '00000' + body.slice(3, 5);
    else if (last === '4') expanded = body.slice(0, 4) + '00000' + body[4];
    else expanded = body.slice(0, 5) + '0000' + last;
    const code = system + expanded + check;
    return hasCheckDigit(code) ? code : null;
}

export function decodeScanLine(values) {
    let low = 255, high = 0;
    for (const value of values) { low = Math.min(low, value); high = Math.max(high, value); }
    if (high - low < 48) return null;
    const threshold = (low + high) / 2;
    const pixels = Array.from(values, value => value < threshold ? '1' : '0').join('');
    return scan(pixels) ?? scan([...pixels].reverse().join(''));
}

function scan(pixels) {
    const starts = [...pixels.matchAll(/0{4,}(1+)(0+)(1+)/g)];
    for (const match of starts) {
        const width = (match[1].length + match[2].length + match[3].length) / 3;
        const offset = match.index + match[0].length - 3 * width;
        if (width < 1.35 || Math.max(match[1].length, match[2].length, match[3].length) > width * 1.65) continue;
        for (const [modules, digits] of [[95, 13], [67, 8], [51, 6]]) {
            const estimate = offset + modules * width;
            const min = Math.max(offset + 45, Math.floor(offset + (estimate - offset) * .85));
            const max = Math.min(pixels.length - 4, Math.ceil(offset + (estimate - offset) * 1.15));
            for (let end = min; end <= max; end++) {
                if (pixels[end - 1] !== '1' || pixels[end] !== '0') continue;
                const unit = (end - offset) / modules;
                if (pixels.slice(end, end + Math.max(4, Math.floor(unit * 4))).includes('1')) continue;
                let bits = '';
                for (let i = 0; i < modules; i++) bits += pixels[Math.floor(offset + (i + .5) * unit)];
                const result = decode(bits, digits);
                if (result) return result;
            }
        }
    }
    return null;
}

export class AndroidBarcodeDetector {
    static async getSupportedFormats() { return ['ean_13', 'ean_8', 'upc_a', 'upc_e']; }

    #canvas = document.createElement('canvas');
    #ctx = this.#canvas.getContext('2d', { willReadFrequently: true });
    #previous = null;
    #checkedAt = 0;

    async detect(video) {
        if (!video.videoWidth || !video.videoHeight || !this.#ctx || performance.now() - this.#checkedAt < 100) return [];
        this.#checkedAt = performance.now();
        const scale = Math.min(1, 960 / Math.max(video.videoWidth, video.videoHeight));
        const width = Math.round(video.videoWidth * scale), height = Math.round(video.videoHeight * scale);
        if (this.#canvas.width !== width || this.#canvas.height !== height) {
            this.#canvas.width = width;
            this.#canvas.height = height;
        }
        this.#ctx.drawImage(video, 0, 0, width, height);
        let rawValue = null;
        for (const ratio of [.48, .42, .55, .36, .61]) {
            const row = this.#ctx.getImageData(0, Math.round(height * ratio), width, 1).data;
            const col = this.#ctx.getImageData(Math.round(width * ratio), 0, 1, height).data;
            rawValue = decodeScanLine(gray(row)) ?? decodeScanLine(gray(col));
            if (rawValue) break;
        }
        const confirmed = rawValue !== null && rawValue === this.#previous;
        this.#previous = rawValue;
        return confirmed ? [{ rawValue }] : [];
    }
}

function gray(rgba) {
    return Uint8Array.from({ length: rgba.length / 4 }, (_, i) =>
        (rgba[i * 4] * 3 + rgba[i * 4 + 1] * 6 + rgba[i * 4 + 2]) / 10);
}
