/**
 * Direct-to-storage image upload.
 *
 * The file never travels through the Blazor circuit: the browser uploads it straight to object
 * storage with a presigned URL. JavaScript therefore owns the <input> element and the drop zone,
 * and reports state back to .NET through plain value callbacks. All limits come from the server so
 * they cannot drift from configuration, and every failure is a stable code the UI localizes.
 *
 * Each field gets its own uploader id so several image fields can coexist on one page.
 */

export const uploadFailureCodes = {
    network: "network",
    invalidType: "invalid_type",
    tooLargeToDownscale: "too_large_to_downscale",
    downscaleFailed: "downscale_failed",
    ticketFailed: "ticket_failed",
    storageRejected: "storage_rejected",
    verifyFailed: "verify_failed",
    canceled: "canceled",
} as const;

export interface UploadLimits {
    maxBytes: number;
    allowedContentTypes: string[];
    /** Longest edge of the downscaled copy. Images already within the size limit are uploaded untouched. */
    maxEdge?: number;
}

export interface UploadCallbacks {
    reportProgressAsync(percent: number): Promise<void>;
    reportSucceededAsync(objectKey: string, previewUrl: string): Promise<void>;
    reportFailedAsync(code: string): Promise<void>;
    reportDragStateAsync(dragging: boolean): Promise<void>;
}

interface UploaderState {
    active: { xhr: XMLHttpRequest | null; canceled: boolean } | null;
    previewUrl: string | null;
}

const DEFAULT_MAX_EDGE = 1600;
const JPEG_QUALITY = 0.92;

const uploaders = new Map<string, UploaderState>();

export function createImageUploader(
    uploaderId: string,
    dropZone: HTMLElement,
    input: HTMLInputElement,
    presignEndpoint: string,
    completeEndpoint: string,
    limits: UploadLimits,
    callbacks: UploadCallbacks): void {
    const state: UploaderState = { active: null, previewUrl: null };
    uploaders.set(uploaderId, state);
    let dragging = false;

    const setDragging = (next: boolean) => {
        if (dragging === next)
            return;
        dragging = next;
        dropZone.classList.toggle("cv-image-dropzone-dragging", next);
        void callbacks.reportDragStateAsync(next);
    };

    dropZone.addEventListener("dragenter", (event) => {
        event.preventDefault();
        setDragging(true);
    });
    dropZone.addEventListener("dragover", (event) => event.preventDefault());
    dropZone.addEventListener("dragleave", () => setDragging(false));
    dropZone.addEventListener("drop", (event) => {
        event.preventDefault();
        setDragging(false);
        const file = event.dataTransfer?.files?.[0];
        if (file !== undefined)
            void start(file);
    });

    input.addEventListener("change", () => {
        const file = input.files?.[0];
        // Reset first so picking the same file again still fires change.
        input.value = "";
        if (file !== undefined)
            void start(file);
    });

    async function start(file: File): Promise<void> {
        if (state.active !== null)
            return;

        revokePreview(state);
        state.previewUrl = URL.createObjectURL(file);
        state.active = { xhr: null, canceled: false };
        try {
            await run(file);
        }
        finally {
            state.active = null;
        }
    }

    async function run(file: File): Promise<void> {
        const canceled = () => state.active?.canceled === true;

        if (!limits.allowedContentTypes.some(type => type.toLowerCase() === file.type.toLowerCase())) {
            await fail(uploadFailureCodes.invalidType);
            return;
        }

        let source = file;
        if (file.size > limits.maxBytes) {
            const scaled = await downscale(file, limits.maxEdge ?? DEFAULT_MAX_EDGE);
            if (scaled === null) {
                await fail(uploadFailureCodes.downscaleFailed);
                return;
            }
            if (scaled.size > limits.maxBytes) {
                await fail(uploadFailureCodes.tooLargeToDownscale);
                return;
            }
            source = scaled;
        }

        const token = getRequestVerificationToken();
        const ticketResult = await postJson(presignEndpoint, token, { contentType: source.type, size: source.size });
        if (ticketResult.kind === "network") {
            await fail(uploadFailureCodes.network);
            return;
        }
        if (ticketResult.kind === "http") {
            await fail(canceled() ? uploadFailureCodes.canceled : uploadFailureCodes.ticketFailed);
            return;
        }
        const ticket = ticketResult.value;
        if (ticket === null || canceled()) {
            await fail(canceled() ? uploadFailureCodes.canceled : uploadFailureCodes.ticketFailed);
            return;
        }

        const put = await putWithProgress(
            state,
            ticket.uploadUrl,
            source,
            ticket.contentType,
            (percent) => void callbacks.reportProgressAsync(percent));
        if (put !== null) {
            await fail(put);
            return;
        }

        const completedResult = await postJson(completeEndpoint, token, {
            objectKey: ticket.objectKey,
            contentType: source.type,
            size: source.size,
        });
        if (completedResult.kind === "network") {
            await fail(uploadFailureCodes.network);
            return;
        }
        if (completedResult.kind === "http") {
            await fail(canceled() ? uploadFailureCodes.canceled : uploadFailureCodes.verifyFailed);
            return;
        }
        const completed = completedResult.value;
        if (completed === null || canceled()) {
            await fail(canceled() ? uploadFailureCodes.canceled : uploadFailureCodes.verifyFailed);
            return;
        }

        await callbacks.reportProgressAsync(100);
        await callbacks.reportSucceededAsync(completed.objectKey, state.previewUrl ?? "");
    }

    async function fail(code: string): Promise<void> {
        // A canceled upload is a user action, not a failure to report.
        if (code === uploadFailureCodes.canceled) {
            revokePreview(state);
            return;
        }
        await callbacks.reportFailedAsync(code);
    }

    async function postJson(url: string, token: string, body: unknown): Promise<{ kind: "ok"; value: any } | { kind: "http" } | { kind: "network" }> {
        try {
            const response = await fetch(url, {
                method: "POST",
                credentials: "same-origin",
                headers: {
                    "Content-Type": "application/json",
                    "X-XSRF-TOKEN": token,
                },
                body: JSON.stringify(body),
            });
            if (!response.ok)
                return { kind: "http" };
            return { kind: "ok", value: await response.json() };
        }
        catch {
            return { kind: "network" };
        }
    }

    function putWithProgress(
        owner: UploaderState,
        url: string,
        body: Blob,
        contentType: string,
        onProgress: (percent: number) => void): Promise<string | null> {
        return new Promise(resolve => {
            const request = new XMLHttpRequest();
            if (owner.active !== null)
                owner.active.xhr = request;
            request.open("PUT", url, true);
            request.setRequestHeader("Content-Type", contentType);
            request.upload.onprogress = (event: ProgressEvent) => {
                if (event.lengthComputable && event.total > 0)
                    onProgress(Math.min(100, Math.round((event.loaded / event.total) * 100)));
            };
            request.onload = () => resolve(
                request.status >= 200 && request.status < 300 ? null : uploadFailureCodes.storageRejected);
            request.onerror = () => resolve(uploadFailureCodes.network);
            request.onabort = () => resolve(uploadFailureCodes.canceled);
            request.send(body);
        });
    }
}

export function cancelUpload(uploaderId: string): void {
    const state = uploaders.get(uploaderId);
    if (state?.active == null)
        return;
    state.active.canceled = true;
    state.active.xhr?.abort();
}

export function releaseUploader(uploaderId: string): void {
    const state = uploaders.get(uploaderId);
    if (state === undefined)
        return;
    if (state.active !== null) {
        state.active.canceled = true;
        state.active.xhr?.abort();
    }
    revokePreview(state);
    uploaders.delete(uploaderId);
}

/**
 * Re-encodes an oversized image at a smaller size using a canvas. Returns null when the browser
 * cannot decode the image, which leaves the caller to report an actionable error.
 */
async function downscale(file: File, maxEdge: number): Promise<Blob | null> {
    const bitmap = await decode(file);
    if (bitmap === null)
        return null;

    const scale = Math.min(1, maxEdge / Math.max(bitmap.width, bitmap.height));
    const width = Math.max(1, Math.round(bitmap.width * scale));
    const height = Math.max(1, Math.round(bitmap.height * scale));

    const canvas = document.createElement("canvas");
    canvas.width = width;
    canvas.height = height;
    const context = canvas.getContext("2d");
    if (context === null)
        return null;
    context.drawImage(bitmap, 0, 0, width, height);
    bitmap.close();

    const blob = await toBlob(canvas, "image/jpeg", JPEG_QUALITY);
    if (blob !== null)
        return blob;

    // A canvas that cannot produce a JPEG means the browser dropped it, usually for memory
    // reasons on very large images. WebP is the next best thing before giving up.
    return await toBlob(canvas, "image/webp", JPEG_QUALITY);
}

async function decode(file: File): Promise<ImageBitmap | null> {
    try {
        return await createImageBitmap(file);
    }
    catch {
        return await decodeViaElement(file);
    }
}

async function decodeViaElement(file: File): Promise<ImageBitmap | null> {
    const url = URL.createObjectURL(file);
    try {
        const image = new Image();
        image.src = url;
        await image.decode();
        return await createImageBitmap(image);
    }
    catch {
        return null;
    }
    finally {
        URL.revokeObjectURL(url);
    }
}

function toBlob(canvas: HTMLCanvasElement, type: string, quality: number): Promise<Blob | null> {
    return new Promise(resolve => canvas.toBlob(resolve, type, quality));
}

function revokePreview(state: UploaderState): void {
    if (state.previewUrl === null)
        return;
    URL.revokeObjectURL(state.previewUrl);
    state.previewUrl = null;
}

function getRequestVerificationToken(): string {
    return document.querySelector<HTMLMetaElement>("meta[name='request-verification-token']")?.content ?? "";
}
