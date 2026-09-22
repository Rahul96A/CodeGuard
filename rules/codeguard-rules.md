# CodeGuard review rules (Australian financial services)

These rules are *inspired by* APRA CPS 234 (information security), the Privacy Act 1988 / APP 11
(security of personal information) and OWASP. They are a learning aid, not legal compliance advice.

| Rule ID | Default severity | What to flag |
|---|---|---|
| CG-SQLI | High | SQL built with string concatenation or interpolation from user input (use parameters, Dapper params or EF Core LINQ). `FromSqlRaw` with interpolated strings counts. |
| CG-SECRET | Critical | Passwords, API keys, tokens, connection strings with credentials, private keys in code or config. |
| CG-DESER | Critical | Insecure deserialization: `BinaryFormatter`, `NetDataContractSerializer`, `LosFormatter`, Json.NET `TypeNameHandling` other than None on untrusted data. |
| CG-AUTHZ | High | Controllers or endpoints handling money, customer data or policies with no `[Authorize]` (check the class and method), or `[AllowAnonymous]` on sensitive actions, or missing ownership checks (IDOR). |
| CG-PII | High | Logging, returning or storing unmasked personal data: TFN, Medicare number, full card number (PAN), BSB + account number, date of birth, full address. Mask or tokenise instead. |
| CG-CRYPTO | High | MD5/SHA1 for passwords or signatures, `System.Random` for tokens/OTPs, hard-coded IVs or keys, ECB mode. |
| CG-TLS | High | Disabled certificate validation (`ServerCertificateCustomValidationCallback` returning true), plain `http://` for external APIs. |
| CG-ERR | Medium | Swallowed exceptions (`catch { }`), returning exception details / stack traces to clients. |
| CG-MONEY | Medium | `double`/`float` used for currency amounts (use `decimal`), missing rounding rules. |
| CG-ASYNC | Low | `.Result` / `.Wait()` on tasks in ASP.NET code, `async void` outside event handlers. |
| CG-TEST | Info | Significant new business logic with no accompanying tests in the diff. |
