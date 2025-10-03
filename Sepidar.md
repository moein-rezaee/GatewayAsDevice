# سپیدار.md — فهرست کامل اندپوینت‌ها (API Level 101)

این سند چک‌لیست عملی همه‌ی اندپوینت‌های وب‌سرویس سپیدار (طبق PDF: sepidar-e-commerce-web-service-v1.0.0.pdf) است؛ به‌همراه دسته‌بندی، متد و خلاصه کاربرد.

## سرخط‌ها و احراز هویت
- هدرهای پایه در اکثر درخواست‌ها پس از لاگین الزامی‌اند:
  - `GenerationVersion`
  - `IntegrationID`
  - `ArbitraryCode` (GUID یکتا در هر درخواست)
  - `EncArbitraryCode` (رمز RSA از بایت‌های GUID با PKCS#1 v1.5)
  - پس از لاگین: `Authorization: Bearer …`
- ثبت دستگاه قبل از لاگین انجام می‌شود؛ لاگین به‌صورت کاربر اختصاص‌داده‌شده به دستگاه است.

## احراز هویت و دستگاه
- POST `/api/Devices/Register` — رجیستر دستگاه (ارسال Cypher/IV/IntegrationID با AES-128-CBC)
- POST `/api/users/login` — لاگین کاربر (هدرهای رمزنگاری + UserName/PasswordHash MD5)
- GET `/api/IsAuthorized` — بررسی وضعیت اعتبار توکن (Authorized)

## عمومی
- GET `/api/General/GenerationVersion` — دریافت ورژن API فعال سیستم

## اطلاعات پایه و عمومی
- GET `/api/AdministrativeDivisions` — دریافت مناطق جغرافیایی
- GET `/api/banks/` — دریافت فهرست بانک‌ها
- GET `/api/BankAccounts` — دریافت حساب‌های بانکی
- GET `/api/Currencies` — دریافت ارزها
- GET `/api/Units` — دریافت واحدهای سنجش
- GET `/api/SaleTypes` — دریافت انواع فروش
- GET `/api/PriceNoteItems` — دریافت اعلامیه‌های قیمت
- GET `/api/properties` — دریافت مشخصات کالا
- GET `/api/Stocks` — دریافت انبارها

## کالاها (Items)
- GET `/api/Items` — دریافت کالاها
- GET `/api/Items/{itemID}/Image/` — دریافت تصویر یک کالا
- GET `/api/Items/Inventories/` — دریافت موجودی کالاها

## مشتری و گروه مشتری
- GET `/api/CustomerGroupings` — دریافت گروه‌های مشتری
- GET `/api/Customers` — دریافت همه مشتری‌ها (لیست)
- GET `/api/Customers/{CustomerID}` — دریافت اطلاعات مشتری خاص
- POST `/api/Customers` — ثبت مشتری
- PUT `/api/Customers/{CustomerID}` — ویرایش مشتری

## پیش‌فاکتور (Quotations)
- GET `/api/Quotations` — دریافت پیش‌فاکتورها (قابلیت فیلتر تاریخ)
- GET `/api/Quotations/{id}` — دریافت پیش‌فاکتور خاص
- POST `/api/Quotations/` — ثبت پیش‌فاکتور
- POST `/api/Quotations/Batch/` — ثبت گروهی پیش‌فاکتور
- POST `/api/Quotations/{quotationID}/Close/` — خاتمه یک پیش‌فاکتور
- POST `/api/Quotations/{quotationID}/UnClose/` — برگشت از خاتمه یک پیش‌فاکتور
- POST `/api/Quotations/Close/Batch` — خاتمه گروهی پیش‌فاکتورها
- POST `/api/Quotations/UnClose/Batch` — برگشت گروهی از خاتمه

## فاکتور فروش (Invoices)
- GET `/api/invoices/` — دریافت فاکتورها
- GET `/api/invoices/{id}` — دریافت فاکتور خاص
- POST `/api/invoices/` — ثبت فاکتور فروش
- POST `/api/Invoices/Batch/` — ثبت گروهی فاکتورهای فروش
- POST `/api/Invoices/BasedOnQuotation/` — ثبت فاکتور بر مبنای پیش‌فاکتور

## دریافت‌ها (Receipts)
- POST `/api/Receipts/BasedOnInvoice/` — ثبت رسید دریافت بر مبنای فاکتور فروش

## یادداشت برای ایجنت (Agent Notes)
- حساسیت به کوچکی/بزرگی حروف در برخی سرورها ممکن است اثر داشته باشد؛ مسیرها را همان‌طور که در PDF آمده استفاده کن.
- ترتیب جریان: Register → Login → حمل هدرها در همه درخواست‌های بعدی → مصرف اندپوینت‌ها.
- در صورت خطای عدم وجود `PublicKey` در رجیستر، ورودی سریال/IntegrationID و الگوریتم کلید مشتق‌شده بررسی شود.
- هنگام ساخت `EncArbitraryCode`: بایت‌های GUID به فرمت RFC4122 رمز شوند؛ Base64 در هدر ارسال شود.

این فهرست مستقیماً از PDF استخراج شده و برای کدنویسی/آزمون سریع آماده است.
