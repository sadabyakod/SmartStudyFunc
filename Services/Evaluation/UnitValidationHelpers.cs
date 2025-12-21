using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace SmartStudyFunc.Services.Evaluation
{
    /// <summary>
    /// PRODUCTION-GRADE: Unit validation and conversion for Physics/Chemistry
    /// Handles SI units, conversions, dimensional analysis
    /// </summary>
    public static class UnitValidationHelpers
    {
        // Comprehensive SI unit conversion tables
        private static readonly Dictionary<string, UnitConversionTable> UnitTables = new()
        {
            ["length"] = new UnitConversionTable
            {
                BaseUnit = "m",
                Dimension = "L",
                Conversions = new()
                {
                    ["km"] = 1000.0, ["m"] = 1.0, ["dm"] = 0.1,
                    ["cm"] = 0.01, ["mm"] = 0.001, ["μm"] = 1e-6, ["nm"] = 1e-9,
                    ["meter"] = 1.0, ["metre"] = 1.0, ["kilometer"] = 1000.0,
                    ["centimeter"] = 0.01, ["millimeter"] = 0.001,
                    // Non-SI but common
                    ["inch"] = 0.0254, ["foot"] = 0.3048, ["ft"] = 0.3048,
                    ["yard"] = 0.9144, ["mile"] = 1609.34
                }
            },
            ["mass"] = new UnitConversionTable
            {
                BaseUnit = "kg",
                Dimension = "M",
                Conversions = new()
                {
                    ["kg"] = 1.0, ["g"] = 0.001, ["mg"] = 1e-6, ["μg"] = 1e-9,
                    ["kilogram"] = 1.0, ["gram"] = 0.001, ["milligram"] = 1e-6,
                    ["ton"] = 1000.0, ["tonne"] = 1000.0,
                    // Non-SI
                    ["lb"] = 0.453592, ["pound"] = 0.453592, ["oz"] = 0.0283495
                }
            },
            ["time"] = new UnitConversionTable
            {
                BaseUnit = "s",
                Dimension = "T",
                Conversions = new()
                {
                    ["s"] = 1.0, ["ms"] = 0.001, ["μs"] = 1e-6, ["ns"] = 1e-9,
                    ["second"] = 1.0, ["millisecond"] = 0.001, ["microsecond"] = 1e-6,
                    ["min"] = 60.0, ["minute"] = 60.0, ["h"] = 3600.0,
                    ["hour"] = 3600.0, ["day"] = 86400.0, ["week"] = 604800.0
                }
            },
            ["force"] = new UnitConversionTable
            {
                BaseUnit = "N",
                Dimension = "MLT^-2",
                Conversions = new()
                {
                    ["N"] = 1.0, ["kN"] = 1000.0, ["mN"] = 0.001,
                    ["newton"] = 1.0, ["kilonewton"] = 1000.0,
                    ["dyne"] = 1e-5, // CGS unit
                    ["lbf"] = 4.44822 // Pound-force
                }
            },
            ["energy"] = new UnitConversionTable
            {
                BaseUnit = "J",
                Dimension = "ML^2T^-2",
                Conversions = new()
                {
                    ["J"] = 1.0, ["kJ"] = 1000.0, ["MJ"] = 1e6, ["mJ"] = 0.001,
                    ["joule"] = 1.0, ["kilojoule"] = 1000.0,
                    ["cal"] = 4.184, ["kcal"] = 4184.0, ["calorie"] = 4.184,
                    ["eV"] = 1.60218e-19, ["keV"] = 1.60218e-16, ["MeV"] = 1.60218e-13,
                    ["Wh"] = 3600.0, ["kWh"] = 3.6e6,
                    ["erg"] = 1e-7 // CGS unit
                }
            },
            ["power"] = new UnitConversionTable
            {
                BaseUnit = "W",
                Dimension = "ML^2T^-3",
                Conversions = new()
                {
                    ["W"] = 1.0, ["kW"] = 1000.0, ["MW"] = 1e6, ["mW"] = 0.001,
                    ["watt"] = 1.0, ["kilowatt"] = 1000.0, ["megawatt"] = 1e6,
                    ["hp"] = 745.7, ["horsepower"] = 745.7
                }
            },
            ["voltage"] = new UnitConversionTable
            {
                BaseUnit = "V",
                Dimension = "ML^2T^-3I^-1",
                Conversions = new()
                {
                    ["V"] = 1.0, ["kV"] = 1000.0, ["MV"] = 1e6, ["mV"] = 0.001,
                    ["volt"] = 1.0, ["kilovolt"] = 1000.0, ["millivolt"] = 0.001
                }
            },
            ["current"] = new UnitConversionTable
            {
                BaseUnit = "A",
                Dimension = "I",
                Conversions = new()
                {
                    ["A"] = 1.0, ["kA"] = 1000.0, ["mA"] = 0.001, ["μA"] = 1e-6,
                    ["ampere"] = 1.0, ["amp"] = 1.0, ["milliampere"] = 0.001
                }
            },
            ["resistance"] = new UnitConversionTable
            {
                BaseUnit = "Ω",
                Dimension = "ML^2T^-3I^-2",
                Conversions = new()
                {
                    ["Ω"] = 1.0, ["ohm"] = 1.0, ["kΩ"] = 1000.0, ["MΩ"] = 1e6,
                    ["kilohm"] = 1000.0, ["megohm"] = 1e6, ["mΩ"] = 0.001
                }
            },
            ["temperature"] = new UnitConversionTable
            {
                BaseUnit = "K",
                Dimension = "Θ",
                Conversions = new()
                {
                    ["K"] = 1.0, ["kelvin"] = 1.0,
                    // Note: Celsius and Fahrenheit require offset conversion
                    ["°C"] = 1.0, ["C"] = 1.0, ["celsius"] = 1.0,
                    ["°F"] = 5.0/9.0, ["F"] = 5.0/9.0, ["fahrenheit"] = 5.0/9.0
                }
            },
            ["pressure"] = new UnitConversionTable
            {
                BaseUnit = "Pa",
                Dimension = "ML^-1T^-2",
                Conversions = new()
                {
                    ["Pa"] = 1.0, ["kPa"] = 1000.0, ["MPa"] = 1e6, ["pascal"] = 1.0,
                    ["bar"] = 1e5, ["mbar"] = 100.0,
                    ["atm"] = 101325.0, ["atmosphere"] = 101325.0,
                    ["mmHg"] = 133.322, ["torr"] = 133.322,
                    ["psi"] = 6894.76
                }
            },
            ["volume"] = new UnitConversionTable
            {
                BaseUnit = "m³",
                Dimension = "L^3",
                Conversions = new()
                {
                    ["m³"] = 1.0, ["m3"] = 1.0, ["cubic meter"] = 1.0,
                    ["L"] = 0.001, ["liter"] = 0.001, ["litre"] = 0.001,
                    ["mL"] = 1e-6, ["milliliter"] = 1e-6, ["ml"] = 1e-6,
                    ["cm³"] = 1e-6, ["cm3"] = 1e-6,
                    ["gallon"] = 0.00378541, ["gal"] = 0.00378541
                }
            },
            ["velocity"] = new UnitConversionTable
            {
                BaseUnit = "m/s",
                Dimension = "LT^-1",
                Conversions = new()
                {
                    ["m/s"] = 1.0, ["m s^-1"] = 1.0,
                    ["km/h"] = 0.277778, ["km h^-1"] = 0.277778,
                    ["mph"] = 0.44704, ["mi/h"] = 0.44704,
                    ["ft/s"] = 0.3048
                }
            },
            ["acceleration"] = new UnitConversionTable
            {
                BaseUnit = "m/s²",
                Dimension = "LT^-2",
                Conversions = new()
                {
                    ["m/s²"] = 1.0, ["m/s^2"] = 1.0, ["m s^-2"] = 1.0,
                    ["ft/s²"] = 0.3048, ["ft/s^2"] = 0.3048,
                    ["g"] = 9.80665 // Standard gravity (note: conflicts with gram, context matters!)
                }
            },
            ["frequency"] = new UnitConversionTable
            {
                BaseUnit = "Hz",
                Dimension = "T^-1",
                Conversions = new()
                {
                    ["Hz"] = 1.0, ["kHz"] = 1000.0, ["MHz"] = 1e6, ["GHz"] = 1e9,
                    ["hertz"] = 1.0, ["kilohertz"] = 1000.0
                }
            },
            ["charge"] = new UnitConversionTable
            {
                BaseUnit = "C",
                Dimension = "IT",
                Conversions = new()
                {
                    ["C"] = 1.0, ["coulomb"] = 1.0, ["mC"] = 0.001, ["μC"] = 1e-6,
                    ["e"] = 1.60218e-19 // Elementary charge
                }
            }
        };

        /// <summary>
        /// Extract value and unit from answer string
        /// Examples: "42.5 m", "9.8 m/s²", "100 N"
        /// </summary>
        public static (double? Value, string Unit) ExtractValueAndUnit(string answer)
        {
            // Pattern: optional sign, number (with decimal), optional unit
            var pattern = @"(?<sign>[+-])?\s*(?<value>\d+\.?\d*)\s*(?<unit>[a-zA-Z°Ω/\^²³⁻]+[\w/\^°Ω\-]*)?";
            var match = Regex.Match(answer.Trim(), pattern);

            if (match.Success)
            {
                var valueStr = match.Groups["value"].Value;
                var sign = match.Groups["sign"].Value == "-" ? -1.0 : 1.0;
                var unit = match.Groups["unit"].Value.Trim();

                if (double.TryParse(valueStr, out var value))
                {
                    return (value * sign, unit);
                }
            }

            return (null, string.Empty);
        }

        /// <summary>
        /// Validate numerical answer with unit tolerance
        /// </summary>
        public static UnitValidationResult ValidateWithUnits(
            string studentAnswer,
            string modelAnswer,
            double tolerancePercent = 2.0)
        {
            var result = new UnitValidationResult();

            var (studentValue, studentUnit) = ExtractValueAndUnit(studentAnswer);
            var (modelValue, modelUnit) = ExtractValueAndUnit(modelAnswer);

            result.StudentValue = studentValue;
            result.StudentUnit = studentUnit;
            result.ModelValue = modelValue;
            result.ModelUnit = modelUnit;

            // Check if values extracted
            if (!studentValue.HasValue || !modelValue.HasValue)
            {
                result.IsValid = false;
                result.Explanation = "Could not extract numerical values";
                result.Confidence = 0.3;
                return result;
            }

            // Convert units to base SI
            var studentInBase = ConvertToBaseUnit(studentValue.Value, studentUnit);
            var modelInBase = ConvertToBaseUnit(modelValue.Value, modelUnit);

            result.StudentValueInBaseUnit = studentInBase.Value;
            result.ModelValueInBaseUnit = modelInBase.Value;
            result.BaseUnit = modelInBase.Unit;

            if (!studentInBase.Success || !modelInBase.Success)
            {
                result.IsValid = false;
                result.Explanation = "Unit conversion failed - unknown unit";
                result.Confidence = 0.5;
                return result;
            }

            // Check unit compatibility
            if (!AreUnitsCompatible(studentUnit, modelUnit))
            {
                result.IsValid = false;
                result.Explanation = $"Unit mismatch: {studentUnit} vs {modelUnit} (incompatible dimensions)";
                result.Confidence = 0.9;
                return result;
            }

            // Compare values in base units
            var difference = Math.Abs(studentInBase.Value - modelInBase.Value);
            var tolerance = Math.Abs(modelInBase.Value) * (tolerancePercent / 100.0);

            result.AbsoluteDifference = difference;
            result.ToleranceUsed = tolerance;

            if (difference <= tolerance)
            {
                result.IsValid = true;
                result.Confidence = 1.0;
                result.Explanation = $"Correct: {studentValue:F2} {studentUnit} = {modelValue:F2} {modelUnit}";
            }
            else
            {
                result.IsValid = false;
                result.Confidence = 0.95;
                result.Explanation = $"Incorrect: Expected {modelValue:F2} {modelUnit}, Got {studentValue:F2} {studentUnit} (diff: {difference:E2})";
            }

            return result;
        }

        /// <summary>
        /// Convert value to base SI unit
        /// </summary>
        public static (double Value, string Unit, bool Success) ConvertToBaseUnit(double value, string unit)
        {
            if (string.IsNullOrWhiteSpace(unit))
            {
                return (value, "unitless", true);
            }

            // Find matching unit table
            foreach (var (category, table) in UnitTables)
            {
                if (table.Conversions.ContainsKey(unit))
                {
                    var conversionFactor = table.Conversions[unit];
                    var baseValue = value * conversionFactor;
                    return (baseValue, table.BaseUnit, true);
                }
            }

            // Handle composite units (e.g., m/s, N/m²)
            if (unit.Contains("/"))
            {
                var parts = unit.Split('/');
                if (parts.Length == 2)
                {
                    var (numeratorValue, numeratorUnit, numSuccess) = ConvertToBaseUnit(value, parts[0].Trim());
                    var (denominatorValue, denominatorUnit, denSuccess) = ConvertToBaseUnit(1.0, parts[1].Trim());

                    if (numSuccess && denSuccess)
                    {
                        var compositeValue = numeratorValue / denominatorValue;
                        var compositeUnit = $"{numeratorUnit}/{denominatorUnit}";
                        return (compositeValue, compositeUnit, true);
                    }
                }
            }

            return (value, unit, false);
        }

        /// <summary>
        /// Check if two units are dimensionally compatible
        /// </summary>
        public static bool AreUnitsCompatible(string unit1, string unit2)
        {
            if (string.IsNullOrWhiteSpace(unit1) && string.IsNullOrWhiteSpace(unit2))
                return true;

            if (string.IsNullOrWhiteSpace(unit1) || string.IsNullOrWhiteSpace(unit2))
                return false;

            // Find dimensions
            var dim1 = GetUnitDimension(unit1);
            var dim2 = GetUnitDimension(unit2);

            return dim1 == dim2 && !string.IsNullOrEmpty(dim1);
        }

        /// <summary>
        /// Get dimensional formula for a unit
        /// </summary>
        private static string GetUnitDimension(string unit)
        {
            foreach (var (category, table) in UnitTables)
            {
                if (table.Conversions.ContainsKey(unit))
                {
                    return table.Dimension;
                }
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Unit conversion table
    /// </summary>
    public class UnitConversionTable
    {
        public string BaseUnit { get; set; } = string.Empty;
        public string Dimension { get; set; } = string.Empty; // Dimensional formula (M, L, T, etc.)
        public Dictionary<string, double> Conversions { get; set; } = new();
    }

    /// <summary>
    /// Result of unit validation
    /// </summary>
    public class UnitValidationResult
    {
        public bool IsValid { get; set; }
        public double Confidence { get; set; }
        public string Explanation { get; set; } = string.Empty;
        public double? StudentValue { get; set; }
        public string StudentUnit { get; set; } = string.Empty;
        public double? ModelValue { get; set; }
        public string ModelUnit { get; set; } = string.Empty;
        public double StudentValueInBaseUnit { get; set; }
        public double ModelValueInBaseUnit { get; set; }
        public string BaseUnit { get; set; } = string.Empty;
        public double AbsoluteDifference { get; set; }
        public double ToleranceUsed { get; set; }
    }
}
