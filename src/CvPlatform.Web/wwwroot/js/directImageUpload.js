const allowedContentTypes = new Set(["image/jpeg", "image/png", "image/webp"]);

export async function uploadImage(input, presignEndpoint, completeEndpoint) {
    const file = input.files?.[0] ?? null;
    if (!file)
        return null;
    input.value = "";
    if (!allowedContentTypes.has(file.type) || file.size <= 0 || file.size > 5 * 1024 * 1024)
        throw new Error("Image type or size is invalid.");

    const token = getRequestVerificationToken();
    const ticketResponse = await fetch(presignEndpoint, {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json",
            "X-XSRF-TOKEN": token,
        },
        body: JSON.stringify({ contentType: file.type, size: file.size }),
    });
    if (!ticketResponse.ok)
        throw new Error(`Upload ticket request failed (${ticketResponse.status}).`);

    const ticket = await ticketResponse.json();
    let uploadResponse;
    try
    {
        uploadResponse = await fetch(ticket.uploadUrl, {
            method: "PUT",
            headers: { "Content-Type": ticket.contentType },
            body: file,
        });
    }
    catch
    {
        throw new Error("Storage request was blocked. Check the bucket CORS policy.");
    }
    if (!uploadResponse.ok)
        throw new Error(`Storage rejected the upload (${uploadResponse.status}).`);

    const completeResponse = await fetch(completeEndpoint, {
        method: "POST",
        credentials: "same-origin",
        headers: {
            "Content-Type": "application/json",
            "X-XSRF-TOKEN": token,
        },
        body: JSON.stringify({
            objectKey: ticket.objectKey,
            contentType: file.type,
            size: file.size,
        }),
    });
    if (!completeResponse.ok)
        throw new Error(`Upload verification failed (${completeResponse.status}).`);

    const completed = await completeResponse.json();
    return completed.objectKey;
}

function getRequestVerificationToken() {
    return document.querySelector("meta[name='request-verification-token']")?.content ?? "";
}
