// ============================================================================
// ScanSystem — توابع کمکی JavaScript (DataTables + دانلود فایل)
// ============================================================================
window.scanSystem = {

    // ─────────── دانلود فایل از یک endpoint با POST (مثلاً ZIP دسته‌ای) ───────────
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
    },

    // ─────────── جدول درخواست‌های اسکن با DataTables (Server-side Processing) ───────────
    initRequestsTable: function (ajaxUrl) {
        if (!window.jQuery || !jQuery.fn || !jQuery.fn.DataTable) {
            console.warn('jQuery/DataTables بارگذاری نشده است (CDN در دسترس نیست؟).');
            return;
        }
        const $ = window.jQuery;
        const el = $('#requestsTable');
        if (!el.length) return;
        if ($.fn.dataTable.isDataTable(el)) return; // جلوگیری از مقداردهی دوباره

        const faLanguage = {
            emptyTable: 'هیچ داده‌ای در جدول وجود ندارد',
            info: 'نمایش _START_ تا _END_ از _TOTAL_ رکورد',
            infoEmpty: 'نمایش 0 تا 0 از 0 رکورد',
            infoFiltered: '(فیلتر شده از _MAX_ رکورد)',
            infoThousands: '٬',
            lengthMenu: 'نمایش _MENU_ رکورد',
            loadingRecords: 'در حال بارگذاری...',
            processing: 'در حال پردازش...',
            search: 'جستجو:',
            zeroRecords: 'رکوردی با این مشخصات یافت نشد',
            paginate: { first: 'اول', last: 'آخر', next: 'بعدی', previous: 'قبلی' },
            aria: { sortAscending: ': مرتب‌سازی صعودی', sortDescending: ': مرتب‌سازی نزولی' }
        };

        const fmtDate = function (d) {
            if (!d) return '—';
            try { return new Date(d).toLocaleString('fa-IR'); }
            catch (e) { return d; }
        };

        const statusBadge = function (s) {
            const map = {
                Pending: ['در انتظار', 'secondary'],
                Processing: ['در حال پردازش', 'warning'],
                Done: ['انجام شد', 'success'],
                Error: ['خطا', 'danger']
            };
            const pair = map[s] || [s || '—', 'light'];
            return '<span class="badge bg-' + pair[1] + '">' + pair[0] + '</span>';
        };

        el.DataTable({
            processing: true,
            serverSide: true,
            filter: true,
            order: [[0, 'desc']],
            pageLength: 10,
            lengthMenu: [10, 25, 50, 100],
            ajax: { url: ajaxUrl, type: 'GET' },
            language: faLanguage,
            columns: [
                { data: 'createdAt', render: fmtDate },
                { data: 'agentMachineName' },
                { data: 'status', render: statusBadge },
                { data: 'isMultiPage', render: function (v) { return v ? 'چندصفحه‌ای' : 'تک‌صفحه'; } },
                { data: 'completedAt', render: fmtDate },
                { data: 'imageCount', orderable: false, searchable: false }
            ]
        });
    },

    destroyRequestsTable: function () {
        if (window.jQuery && jQuery.fn.dataTable && jQuery.fn.dataTable.isDataTable('#requestsTable')) {
            jQuery('#requestsTable').DataTable().destroy();
        }
    }
};
