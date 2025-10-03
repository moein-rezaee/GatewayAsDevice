# AGENTS.md — راهنمای عامل (Agent) برای کار با پروژه

هدف این فایل: خلاصه‌ی عملی و اجریی از معماری، تنظیمات، جریان‌ها و قراردادهای کدنویسی این ریپو تا عامل‌ها و توسعه‌دهندگان بدون سردرگمی کار را ادامه دهند.

## نمای کلی
- ماهیت پروژه: یک Gateway سبک برای رجیستر دستگاه در سپیدار و لاگین کاربر و بازگرداندن پاسخِ غنی‌شده.
- زبان/فریم‌ورک: .NET 9 (Minimal API) + کتابخانه افزونه HTTP داخلی.
- مستند مرجع: `sepidar-e-commerce-web-service-v1.0.0.pdf` (API Level 101).
- نقطه ورود سرویس: `GatewayAsDevice.Api/Program.cs`.

## ساختار پوشه‌ها (مهم)
- `GatewayAsDevice.Api/`
  - Endpoints: `Endpoints/Device/RegisterDeviceEndpoints.cs`, `Endpoints/Gateway/HealthEndpoints.cs`
  - Services: `Services/SepidarService.cs`, `Services/CurlBuilder.cs`
  - Models/Interfaces/Config: `Models/*.cs`, `Interfaces/*.cs`, `appsettings.json`
- `Extensions/HttpClientRestExtension/`: کلاینت HTTP ساده و اتصالات آن
- `Extensions/SepidarExtension/`: منطق رجیستر/لاگین سپیدار و رمزنگاری
  - `Services/SepidarClient.cs`, `SerialKeyDeriver.cs`, `PublicKeyProcessor.cs`, `IntegrationIdExtractor.cs`, `SepidarClientOptions.cs`
- `PythonSample/`: نمونه‌های پایتونی مصرف سرویس سپیدار (مرجع مفهومی)
- ریشه ریپو: `.env`, `sepidar-e-commerce-web-service-v1.0.0.pdf`, `docker-compose.yml`, `Dockerfile`

یادداشت: فایل‌های Docker/Docker Compose فعلی ظاهراً با نام‌های پروژه هم‌خوان نیستند (ارجاع به SepidarGateway*). برای اجرا، مسیر Dotnet محلی را ترجیح دهید مگر این ناسازگاری‌ها اصلاح شوند.

## اجرا (لوکال)
- پیش‌نیازها: `dotnet 9`, دسترسی شبکه به BaseUrl سپیدار، تنظیم `.env`.
- متغیرهای `.env`:
  - `LOGIN_USERNAME=...`
  - `LOGIN_PASSWORD=...`
- پیکربندی‌های کلیدی در `GatewayAsDevice.Api/appsettings.json`:
  - `Sepidar:BaseUrl`: آدرس پایه سپیدار (اجباری)
  - `Sepidar:RegisterDevice:Endpoint`: پیش‌فرض `/api/Devices/Register`
  - `Sepidar:RegisterDevice:IntegrationIdLength`: پیش‌فرض `4`
  - `Sepidar:UsersLogin:Endpoint`: پیش‌فرض `/api/users/login`
  - `Gateway:Swagger:*`: تنظیم عنوان/مسیر Swagger
- اجرا:
  - `dotnet run --project GatewayAsDevice.Api`
  - Swagger در مسیر `/{RoutePrefix یا پیش‌فرض swagger}` در دسترس است.

## سطوح و مسیرهای API (Gateway)
- سلامت: `GET /health` (قابل تنظیم با `Gateway:Health:Path`)
- رجیستر دستگاه و لاگین: `POST /v1/{Sepidar:RegisterDevice:Endpoint}`
  - بدنه دروازه: `{ "serial": "..." }`
  - خروجی: آبجکت JSON شامل `Register`, `Login`, `GenerationVersion` و در صورت تشخیص توکن، `Authorization`
  - `GET` روی همین مسیر: خطای هدایت به `POST`

## جریان رجیستر + لاگین (مطابق PDF)
1) رجیستر دستگاه
   - استخراج `IntegrationID` از سریال (طول پیش‌فرض ۴)
   - مشتق کلید ۱۶کاراکتری از سریال (AES-128-CBC)
   - رمز IntegrationID → تولید `Cypher` و `IV` (Base64)
   - درخواست به `{BaseUrl} + {RegisterEndpoint}` با بدنه:
     - `{ Cypher, IV, IntegrationID }`
   - انتظار دریافت کلید عمومی RSA (به‌صورت XML یا اجزای Modulus/Exponent)

2) لاگین کاربر
   - تولید `ArbitraryCode = GUID` و تبدیل به بایت‌های RFC4122
   - رمز بایت‌ها با RSA (Padding: PKCS1) ← `EncArbitraryCode` (Base64)
   - محاسبه `PasswordHash = MD5(Password).ToHexLower()`
   - هدرها: `GenerationVersion`, `IntegrationID`, `ArbitraryCode`, `EncArbitraryCode`
   - بدنه: `{ UserName, PasswordHash }`
   - پاسخ: توکن یا فیلدهای مرتبط؛ در صورت وجود، هدر `Authorization = Bearer {token}` در خروجی گیت‌وی اضافه می‌شود

ارجاعات کد:
- پیاده‌سازی End-to-End: `GatewayAsDevice.Api/Services/SepidarService.cs`
- کلاینت سپیدار: `Extensions/SepidarExtension/Services/SepidarClient.cs`
- رمزنگاری/کلیدها: `SerialKeyDeriver.cs`, `PublicKeyProcessor.cs`

## جزئیات رمزنگاری (خلاصه عملی)
- AES-128-CBC + PKCS7 برای رمز IntegrationID
  - Key: ۱۶ کاراکتر از سریال (دو برابر/برش یا تکرار) — پیاده‌سازی در `SerialKeyDeriver`
  - IV: تصادفی و Base64 در درخواست رجیستر ارسال می‌شود
- RSA (PublicKey از رجیستر)
  - Encrypt: بایت‌های GUID به صورت RFC4122 + `RSAEncryptionPadding.Pkcs1`
  - استخراج پارامترها از XML یا فیلد `PublicKey` (Modulus/Exponent Base64)

## مدیریت خطاها (Gateway)
- پیام‌های کاربرپسند و فارسی در خروجی API
- نگاشت استثناها:
  - ورودی نامعتبر: 400 BadRequest
  - خطاهای داخلی/پیکربندی: 500 Problem
  - خطای ارتباط با سپیدار: 502 BadGateway
  - تایم‌اوت: 504 GatewayTimeout

## Swagger
- فعال و قابل دسترس؛ عنوان و مسیر از `Gateway:Swagger` قابل پیکربندی است.

## الگوی توسعه و کدنویسی
- تغییرات حداقلی، هم‌خوان با سبک فعلی فایل‌ها
- پیام‌های خطا و متن‌ها فارسی؛ نام‌گذاری کلاس‌ها و نام‌فضاها انگلیسی PascalCase
- عدم افزودن وابستگی غیرضروری؛ استفاده از سرویس‌های موجود (`IHttpRestClient`, ...)
- از ویرایش فایل‌های Docker موجود بدون هماهنگی خودداری کنید (عدم انطباق فعلی با نام پروژه را لحاظ کنید)

## چک‌لیست اضافه‌/تغییر فیچر مرتبط با سپیدار
- [ ] کلیدهای پیکربندی موردنیاز در `appsettings*.json` تعریف شده‌اند
- [ ] وابستگی‌ها در DI ثبت شده‌اند (`Program.cs`)
- [ ] هندلر Endpoint خلاصه و واضح، ارورها صریح
- [ ] خروجی JSON با فیلدهای افزوده‌ی مفید (IntegrationID/Authorization در صورت وجود)
- [ ] curl نمونه (در صورت نیاز) با `CurlBuilder` تولیدپذیر است

## نمونه درخواست‌ها
- رجیستر + لاگین از طریق گیت‌وی:
  - `POST http://localhost:5090/v1/api/Devices/Register`
  - Body: `{ "serial": "ABC123456" }`
- هدرهای لازم سمت گیت‌وی به‌صورت خودکار توسط سرویس‌ها ساخته می‌شود؛ نیازی به ارسال آن‌ها از کلاینت نیست.

## تنظیمات و اسرار
- نام‌کاربری/گذرواژه فقط از `.env` خوانده می‌شود: `LOGIN_USERNAME`, `LOGIN_PASSWORD`
- از لاگ‌کردن مقادیر حساس خودداری کنید

## تست و عیب‌یابی
- بررسی سلامت: `GET /health`
- فعال‌بودن Swagger و دسترسی به `/swagger` را کنترل کنید
- اگر رجیستر کلید عمومی برنگرداند:
  - سریال/IntegrationID/الگوریتم کلید مشتق‌شده را بررسی کنید
  - شبکه و `BaseUrl` را بررسی کنید
- تایم‌اوت‌ها را با لاگ و شبکه مقصد چک کنید

## نکات PDF که باید به خاطر بسپاریم
- سطح API: 101؛ برخی نام فیلدها/مسیرها حساس به کوچک/بزرگ هستند
- رجیستر: بدنه شامل `Cypher`, `IV`, `IntegrationID`
- لاگین: هدرها شامل `GenerationVersion`, `IntegrationID`, `ArbitraryCode`, `EncArbitraryCode` و بدنه با `UserName`, `PasswordHash`

## موارد ناسازگاری/هشدار
- `Dockerfile` و `docker-compose.yml` به نام‌های `SepidarGateway.*` اشاره دارند، درحالی‌که پروژه فعلی `GatewayAsDevice.*` است. تا رفع این اختلاف، اجرای لوکال توصیه می‌شود.

---
این فایل مرجع سریع عامل/توسعه‌دهنده است؛ در صورت اضافه‌شدن فیچرها یا تغییر پیکربندی، این سند را به‌روزرسانی کنید.

