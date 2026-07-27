// ============================================================================
// ScanSystem — توابع کمکی JavaScript خالص (بدون jQuery / بدون کتابخانه جانبی)
// ============================================================================
window.scanSystem = {

    // دانلود فایل از یک endpoint با POST (مثلاً ZIP دسته‌ای گالری)
    postDownload: async function (url, bodyJson, fileName) {
        try {
            const resp = await fetch(url, {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: bodyJson
            });
            if (!resp.ok) return false;
            const blob = await resp.blob();
            const a = document.createElement('a');
            a.href = URL.createObjectURL(blob);
            a.download = fileName || 'download.bin';
            document.body.appendChild(a);
            a.click();
            a.remove();
            setTimeout(function () { URL.revokeObjectURL(a.href); }, 4000);
            return true;
        } catch (e) {
            console.error('postDownload failed:', e);
            return false;
        }
    }
};
