using GalaxyPPG.Common;

namespace GalaxyPPG.Server
{
    /// <summary>
    /// Statička klasa koja sprovodi validaciona pravila iz KT1 zadatka 3:
    ///   - TimestampUnix &gt; 0
    ///   - BVP u opsegu [-2000, 2000]
    ///   - SkinTemp u opsegu [20, 45] °C
    ///   - IBI_ms u opsegu [250, 2000] ms
    ///   - NaN se tretira kao "missing" -&gt; ide u rejects.
    /// </summary>
    public static class SampleValidator
    {
        // Granice po specifikaciji - centralizovane kao konstante da bi se
        // pravila lakše pratila i menjala iz jednog mesta.
        public const double BvpMin = -2000.0;
        public const double BvpMax = 2000.0;
        public const double SkinTempMinC = 20.0;
        public const double SkinTempMaxC = 45.0;
        public const double IbiMinMs = 250.0;
        public const double IbiMaxMs = 2000.0;

        public static ValidationResult Validate(E4Sample sample)
        {
            if (sample == null)
            {
                return ValidationResult.Failure("NULL_SAMPLE", "Sample is null.", null);
            }

            // Provera vremena - 0 ili negativna vrednost je sigurno greška u parsiranju.
            if (sample.TimestampUnix <= 0)
            {
                return ValidationResult.Failure(
                    "TIMESTAMP_OUT_OF_RANGE",
                    "TimestampUnix must be greater than 0.",
                    nameof(E4Sample.TimestampUnix));
            }

            // BVP: validacija samo ako vrednost nije null (NaN je već mapiran u null
            // na klijentu). Eksplicitno hvatamo NaN za slučaj da je provukao kroz mapiranje.
            if (sample.BVP.HasValue)
            {
                if (double.IsNaN(sample.BVP.Value))
                {
                    return ValidationResult.Failure(
                        "NAN_VALUE",
                        "BVP is NaN (treated as missing).",
                        nameof(E4Sample.BVP));
                }

                if (sample.BVP.Value < BvpMin || sample.BVP.Value > BvpMax)
                {
                    return ValidationResult.Failure(
                        "BVP_OUT_OF_RANGE",
                        $"BVP must be within [{BvpMin}, {BvpMax}].",
                        nameof(E4Sample.BVP));
                }
            }

            if (sample.SkinTemp.HasValue)
            {
                if (double.IsNaN(sample.SkinTemp.Value))
                {
                    return ValidationResult.Failure(
                        "NAN_VALUE",
                        "SkinTemp is NaN (treated as missing).",
                        nameof(E4Sample.SkinTemp));
                }

                if (sample.SkinTemp.Value < SkinTempMinC || sample.SkinTemp.Value > SkinTempMaxC)
                {
                    return ValidationResult.Failure(
                        "SKIN_TEMP_OUT_OF_RANGE",
                        $"SkinTemp must be within [{SkinTempMinC}, {SkinTempMaxC}] C.",
                        nameof(E4Sample.SkinTemp));
                }
            }

            if (sample.IBI_ms.HasValue)
            {
                if (double.IsNaN(sample.IBI_ms.Value))
                {
                    return ValidationResult.Failure(
                        "NAN_VALUE",
                        "IBI_ms is NaN (treated as missing).",
                        nameof(E4Sample.IBI_ms));
                }

                if (sample.IBI_ms.Value < IbiMinMs || sample.IBI_ms.Value > IbiMaxMs)
                {
                    return ValidationResult.Failure(
                        "IBI_OUT_OF_RANGE",
                        $"IBI_ms must be within [{IbiMinMs}, {IbiMaxMs}] ms.",
                        nameof(E4Sample.IBI_ms));
                }
            }

            // Za ostale opcione kanale (ACC i HR) eksplicitno proveravamo NaN
            // kako bi i oni završili u rejects, kako specifikacija nalaže.
            if (sample.AccX.HasValue && double.IsNaN(sample.AccX.Value))
                return ValidationResult.Failure("NAN_VALUE", "AccX is NaN.", nameof(E4Sample.AccX));
            if (sample.AccY.HasValue && double.IsNaN(sample.AccY.Value))
                return ValidationResult.Failure("NAN_VALUE", "AccY is NaN.", nameof(E4Sample.AccY));
            if (sample.AccZ.HasValue && double.IsNaN(sample.AccZ.Value))
                return ValidationResult.Failure("NAN_VALUE", "AccZ is NaN.", nameof(E4Sample.AccZ));
            if (sample.HeartRate.HasValue && double.IsNaN(sample.HeartRate.Value))
                return ValidationResult.Failure("NAN_VALUE", "HeartRate is NaN.", nameof(E4Sample.HeartRate));

            return ValidationResult.Success();
        }
    }

    /// <summary>
    /// Rezultat validacije - ili uspeh ili tačan razlog odbijanja.
    /// Imutabilan po dizajnu (private setteri + factory metode).
    /// </summary>
    public sealed class ValidationResult
    {
        public bool IsValid { get; private set; }
        public string Code { get; private set; }
        public string Message { get; private set; }
        public string Field { get; private set; }

        private ValidationResult() { }

        public static ValidationResult Success() => new ValidationResult { IsValid = true };

        public static ValidationResult Failure(string code, string message, string field)
        {
            return new ValidationResult
            {
                IsValid = false,
                Code = code,
                Message = message,
                Field = field
            };
        }
    }
}
