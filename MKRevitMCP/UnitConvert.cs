using Autodesk.Revit.DB;

namespace MKRevitMCP
{
    // Revit stores every length internally in feet, whatever the project units say.
    // Convert at the boundary: tools take and return millimetres, Revit gets feet.
    public static class UnitConvert
    {
        public static double FromMm(double millimetres)
        {
            return UnitUtils.ConvertToInternalUnits(millimetres, UnitTypeId.Millimeters);
        }

        public static double ToMm(double internalFeet)
        {
            return UnitUtils.ConvertFromInternalUnits(internalFeet, UnitTypeId.Millimeters);
        }
    }
}
