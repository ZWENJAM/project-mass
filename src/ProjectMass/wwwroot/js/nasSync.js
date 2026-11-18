import { get, put, delete as remove } from './db.js';

const store = 'syncMetadata';
const handleId = 'nas-folder';
const permission = { mode: 'readwrite' };
let handle;

export function isFolderSupported() {
    return typeof window.showDirectoryPicker === 'function';
}

export function defaultDeviceName() {
    return /Android|iPhone|iPad|Mobile/i.test(navigator.userAgent) ? 'Handy' : 'PC';
}

export async function loadFolder() {
    handle = (await get(store, handleId))?.handle;
    return handle?.name ?? null;
}

export async function chooseFolder() {
    try {
        const selected = await window.showDirectoryPicker(permission);
        await put(store, { id: handleId, handle: selected });
        handle = selected;
        return handle.name;
    } catch (error) {
        if (error.name === 'AbortError') return null;
        throw error;
    }
}

export async function forgetFolder() {
    await remove(store, handleId);
    handle = undefined;
}

export async function hasFolderPermission() {
    return !!handle && await handle.queryPermission(permission) === 'granted';
}

export async function requestFolderPermission() {
    return !!handle && await handle.requestPermission(permission) === 'granted';
}

async function directory(parts, create) {
    let current = handle;
    for (const part of parts) {
        current = await current.getDirectoryHandle(part, { create });
    }
    return current;
}

function segments(path) {
    const parts = path.split('/');
    if (parts.some(part => !part || part === '.' || part === '..' || part.includes('\\'))) {
        throw new Error('Ungültiger Pfad zum NAS.');
    }
    return parts;
}

async function readFile(dir, name) {
    try { return await (await (await dir.getFileHandle(name)).getFile()).text(); }
    catch (error) {
        if (error.name === 'NotFoundError') return null;
        throw error;
    }
}

export async function folderRequest(method, path, content, conditional = false, expected = null) {
    try {
        if (!await hasFolderPermission()) return { permission: false };
        const parts = segments(path);
        if (method === 'list') {
            const files = [];
            for await (const entry of (await directory(parts, false)).values()) {
                if (entry.kind === 'file') files.push(entry.name);
            }
            return { permission: true, files };
        }
        const name = parts.pop();
        const dir = await directory(parts, method === 'write');
        if (method === 'read') return { permission: true, content: await readFile(dir, name) };
        if (method === 'delete') {
            await dir.removeEntry(name);
            return { permission: true };
        }
        if (conditional && await readFile(dir, name) !== expected) return { permission: true, matched: false };
        const file = await dir.getFileHandle(name, { create: true });
        const writer = await file.createWritable({ mode: 'exclusive' });
        try {
            if (conditional && await readFile(dir, name) !== (expected ?? '')) {
                await writer.abort();
                return { permission: true, matched: false };
            }
            await writer.write(content);
            await writer.close();
        } catch (error) {
            try { await writer.abort(); } catch { /* The writer may have closed already. */ }
            throw error;
        }
        return { permission: true, matched: true };
    } catch (error) {
        if (error.name === 'NotFoundError' && method !== 'write') return { permission: true };
        if (error.name === 'NotAllowedError' || error.name === 'SecurityError') return { permission: false };
        return { permission: true, failed: true };
    }
}

export async function webDavRequest(url, method, username, password, content, headers) {
    const bytes = new TextEncoder().encode(username + ':' + password);
    const authorization = 'Basic ' + btoa(Array.from(bytes, b => String.fromCharCode(b)).join(''));
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 15000);
    try {
        const response = await fetch(url, {
            method, headers: { ...headers, Authorization: authorization }, body: content ?? undefined,
            credentials: 'omit', redirect: 'error', cache: 'no-store', referrerPolicy: 'no-referrer',
            signal: controller.signal
        });
        return {
            status: response.status,
            content: response.ok && (method === 'GET' || method === 'PROPFIND') ? await response.text() : null,
            etag: response.headers.get('ETag')
        };
    } catch {
        return { status: controller.signal.aborted ? -1 : 0 };
    } finally {
        clearTimeout(timeout);
    }
}

export function watchVisibility(receiver) {
    const listener = () => {
        if (!document.hidden) receiver.invokeMethodAsync('BecameVisible').catch(() => {});
    };
    document.addEventListener('visibilitychange', listener);
}
