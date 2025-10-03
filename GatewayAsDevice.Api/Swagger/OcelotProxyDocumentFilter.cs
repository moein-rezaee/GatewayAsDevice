using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace SepidarGateway.Api.Swagger;

public class OcelotProxyDocumentFilter : IDocumentFilter
{
    public void Apply(OpenApiDocument swaggerDoc, DocumentFilterContext context)
    {
        OpenApiSchema Str(string? fmt = null, bool nullable = false) => new()
        {
            Type = "string",
            Format = fmt,
            Nullable = nullable
        };
        OpenApiSchema Int() => new() { Type = "integer", Format = "int32" };
        OpenApiSchema Num() => new() { Type = "number", Format = "double" };

        void Add(string path, string method, string tag, string summary,
            Action<OpenApiOperation>? configure = null)
        {
            if (!swaggerDoc.Paths.TryGetValue(path, out var item))
            {
                item = new OpenApiPathItem();
                swaggerDoc.Paths[path] = item;
            }
            var op = new OpenApiOperation
            {
                Summary = summary,
                Tags = new List<OpenApiTag> { new() { Name = tag } },
                Responses = new OpenApiResponses { ["200"] = new OpenApiResponse { Description = "OK" } }
            };
            op.Description = "Headers لازم به صورت خودکار تزریق می‌شوند (نیازی به ارسال از سمت کلاینت نیست).";
            configure?.Invoke(op);
            switch (method.ToUpperInvariant())
            {
                case "GET": item.Operations[OperationType.Get] = op; break;
                case "POST": item.Operations[OperationType.Post] = op; break;
                case "PUT": item.Operations[OperationType.Put] = op; break;
            }
        }

        // Internal endpoints (Register/Health) از خود Minimal API سند می‌گیرند؛ اینجا اضافه نمی‌کنیم.

        // General & Auth
        Add("/v1/api/General/GenerationVersion", "GET", "General", "دریافت ورژن API");
        Add("/v1/api/IsAuthorized", "GET", "Auth", "بررسی وضعیت اعتبار توکن");

        // BaseData
        Add("/v1/api/AdministrativeDivisions", "GET", "BaseData", "دریافت مناطق جغرافیایی");
        Add("/v1/api/Banks/", "GET", "BaseData", "دریافت بانک‌ها");
        Add("/v1/api/BankAccounts", "GET", "BaseData", "دریافت حساب‌های بانکی");
        Add("/v1/api/Currencies", "GET", "BaseData", "دریافت ارزها");
        Add("/v1/api/Units", "GET", "BaseData", "دریافت واحدها");
        Add("/v1/api/SaleTypes", "GET", "BaseData", "دریافت انواع فروش");
        Add("/v1/api/PriceNoteItems", "GET", "BaseData", "دریافت اعلامیه‌های قیمت");
        Add("/v1/api/Properties", "GET", "BaseData", "دریافت مشخصات کالا");
        Add("/v1/api/Stocks", "GET", "BaseData", "دریافت انبارها");

        // Items
        Add("/v1/api/Items", "GET", "Items", "دریافت کالاها");
        Add("/v1/api/Items/{ItemID}/Image/", "GET", "Items", "دریافت تصویر کالا", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "ItemID", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه کالا" }
            };
        });
        Add("/v1/api/Items/Inventories/", "GET", "Items", "دریافت موجودی کالاها");

        // Customers
        Add("/v1/api/CustomerGroupings", "GET", "Customers", "دریافت گروه‌های مشتری");
        Add("/v1/api/Customers", "GET", "Customers", "دریافت مشتری‌ها");
        Add("/v1/api/Customers/{CustomerID}", "GET", "Customers", "دریافت مشتری خاص", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "CustomerID", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه مشتری" }
            };
        });
        Add("/v1/api/Customers", "POST", "Customers", "ثبت مشتری", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["GUID"] = Str(),
                                ["PhoneNumber"] = Str(nullable: true),
                                ["CustomerType"] = Int(),
                                ["Name"] = Str(),
                                ["LastName"] = Str(),
                                ["BirthDate"] = Str("date-time", true),
                                ["NationalID"] = Str(nullable: true),
                                ["EconomicCode"] = Str(nullable: true),
                                ["Addresses"] = new OpenApiSchema
                                {
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["Title"] = Str(),
                                            ["CityRef"] = Int(),
                                            ["Address"] = Str(),
                                            ["PostalCode"] = Str(nullable: true)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        });
        Add("/v1/api/Customers/{CustomerID}", "PUT", "Customers", "ویرایش مشتری", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "CustomerID", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه مشتری" }
            };
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["PhoneNumber"] = Str(nullable: true),
                                ["BirthDate"] = Str("date-time", true),
                                ["NationalID"] = Str(nullable: true),
                                ["EconomicCode"] = Str(nullable: true),
                                ["Version"] = Int(),
                                ["Addresses"] = new OpenApiSchema
                                {
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["Title"] = Str(),
                                            ["CityRef"] = Int(),
                                            ["Address"] = Str(),
                                            ["PostalCode"] = Str(nullable: true)
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        });

        // Quotations
        Add("/v1/api/Quotations", "GET", "Quotations", "دریافت پیش‌فاکتورها", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "FromDate", In = ParameterLocation.Query, Required = false, Schema = Str("date-time", true), Description = "از تاریخ (ISO 8601)" },
                new() { Name = "ToDate", In = ParameterLocation.Query, Required = false, Schema = Str("date-time", true), Description = "تا تاریخ (ISO 8601)" }
            };
        });
        Add("/v1/api/Quotations/{Id}", "GET", "Quotations", "دریافت پیش‌فاکتور خاص", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "Id", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه پیش‌فاکتور" }
            };
        });
        Add("/v1/api/Quotations/", "POST", "Quotations", "ثبت پیش‌فاکتور", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["GUID"] = Str(),
                                ["CurrencyRef"] = Int(),
                                ["Rate"] = Num(),
                                ["Date"] = Str("date-time"),
                                ["Description"] = Str(nullable: true),
                                ["Items"] = new OpenApiSchema
                                {
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["ItemRef"] = Int(),
                                            ["Quantity"] = Num(),
                                            ["UnitPrice"] = Num(),
                                            ["Discount"] = Num()
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        });
        Add("/v1/api/Quotations/Batch/", "POST", "Quotations", "ثبت گروهی پیش‌فاکتور", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "array",
                            Items = swaggerDoc.Paths["/v1/api/Quotations/"].Operations[OperationType.Post].RequestBody.Content["application/json"].Schema
                        }
                    }
                }
            };
        });
        Add("/v1/api/Quotations/{QuotationID}/Close/", "POST", "Quotations", "خاتمه پیش‌فاکتور", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "QuotationID", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه پیش‌فاکتور" }
            };
        });
        Add("/v1/api/Quotations/{QuotationID}/UnClose/", "POST", "Quotations", "بازگشت از خاتمه پیش‌فاکتور", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "QuotationID", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه پیش‌فاکتور" }
            };
        });
        Add("/v1/api/Quotations/Close/Batch", "POST", "Quotations", "خاتمه گروهی پیش‌فاکتورها", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["Ids"] = new OpenApiSchema { Type = "array", Items = Int() }
                            }
                        }
                    }
                }
            };
        });
        Add("/v1/api/Quotations/UnClose/Batch", "POST", "Quotations", "بازگشت گروهی از خاتمه", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["Ids"] = new OpenApiSchema { Type = "array", Items = Int() }
                            }
                        }
                    }
                }
            };
        });

        // Invoices
        Add("/v1/api/Invoices/", "GET", "Invoices", "دریافت فاکتورها");
        Add("/v1/api/Invoices/{Id}", "GET", "Invoices", "دریافت فاکتور خاص", op =>
        {
            op.Parameters = new List<OpenApiParameter>
            {
                new() { Name = "Id", In = ParameterLocation.Path, Required = true, Schema = Int(), Description = "شناسه فاکتور" }
            };
        });
        Add("/v1/api/Invoices/", "POST", "Invoices", "ثبت فاکتور فروش", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["GUID"] = Str(),
                                ["CurrencyRef"] = Int(),
                                ["CustomerRef"] = Int(),
                                ["AddressRef"] = Int(),
                                ["SaleTypeRef"] = Int(),
                                ["Date"] = Str("date-time"),
                                ["Items"] = new OpenApiSchema
                                {
                                    Type = "array",
                                    Items = new OpenApiSchema
                                    {
                                        Type = "object",
                                        Properties = new Dictionary<string, OpenApiSchema>
                                        {
                                            ["ItemRef"] = Int(),
                                            ["Quantity"] = Num(),
                                            ["UnitPrice"] = Num(),
                                            ["Discount"] = Num()
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            };
        });
        Add("/v1/api/Invoices/Batch/", "POST", "Invoices", "ثبت گروهی فاکتورها", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "array",
                            Items = swaggerDoc.Paths["/v1/api/Invoices/"].Operations[OperationType.Post].RequestBody.Content["application/json"].Schema
                        }
                    }
                }
            };
        });
        Add("/v1/api/Invoices/BasedOnQuotation/", "POST", "Invoices", "ثبت فاکتور بر مبنای پیش‌فاکتور", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["QuotationID"] = Int()
                            }
                        }
                    }
                }
            };
        });

        // Receipts
        Add("/v1/api/Receipts/BasedOnInvoice/", "POST", "Receipts", "ثبت رسید بر مبنای فاکتور", op =>
        {
            op.RequestBody = new OpenApiRequestBody
            {
                Required = true,
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = new OpenApiSchema
                        {
                            Type = "object",
                            Properties = new Dictionary<string, OpenApiSchema>
                            {
                                ["GUID"] = Str(),
                                ["Date"] = Str("date-time"),
                                ["Description"] = Str(nullable: true),
                                ["Discount"] = Num(),
                                ["InvoiceID"] = Int(),
                                ["Amount"] = Num(),
                                ["BankAccountID"] = Int()
                            }
                        }
                    }
                }
            };
        });
    }
}
