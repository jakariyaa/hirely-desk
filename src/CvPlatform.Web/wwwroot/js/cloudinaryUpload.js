let scriptPromise = null;

function ensureWidgetScript() {
    if (window.cloudinary?.createUploadWidget)
        return Promise.resolve();
    if (scriptPromise)
        return scriptPromise;
    scriptPromise = new Promise((resolve, reject) => {
        const script = document.createElement("script");
        script.src = "https://upload-widget.cloudinary.com/latest/global/all.js";
        script.async = true;
        script.onload = () => resolve();
        script.onerror = () => reject(new Error("Cloudinary widget failed to load."));
        document.head.appendChild(script);
    }).catch(error => {
        scriptPromise = null;
        throw error;
    });
    return scriptPromise;
}

export async function openUploadWidget(cloudName, uploadPreset, signatureEndpoint) {
    await ensureWidgetScript();
    const signed = signatureEndpoint
        ? await fetch(signatureEndpoint, { credentials: "same-origin" }).then(async response => {
            if (!response.ok)
                throw new Error("Image upload is not configured.");
            return response.json();
        })
        : null;
    return new Promise((resolve, reject) => {
        const options = {
            cloudName,
            multiple: false,
            maxFileSize: 5 * 1024 * 1024,
            clientAllowedFormats: ["jpg", "jpeg", "png", "webp"],
            sources: ["local"],
            resourceType: "image",
        };
        if (signed) {
            options.apiKey = signed.apiKey;
            options.uploadSignature = signed.signature;
            options.uploadSignatureTimestamp = signed.timestamp;
            options.folder = signed.folder;
            options.publicId = signed.publicId;
        } else {
            options.uploadPreset = uploadPreset;
        }
        const widget = window.cloudinary.createUploadWidget(
            options,
            (error, result) => {
                if (error) {
                    reject(error instanceof Error ? error : new Error("Upload failed."));
                    return;
                }
                if (result?.event === "success" &&
                    result.info?.resource_type === "image" &&
                    result.info?.secure_url) {
                    widget.close();
                    resolve(result.info.secure_url);
                } else if (result?.event === "close") {
                    resolve(null);
                }
            });
        widget.open();
    });
}
