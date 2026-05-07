namespace Plotto.Core.Interaction;

public interface IChartWheelZoomPolicy
{
    bool ShouldZoomXAxis(bool ctrlPressed, bool shiftPressed, bool pointerOverXAxisScrollZone);
    double GetZoomFactor(int delta);
}

public sealed class ChartWheelZoomPolicy : IChartWheelZoomPolicy
{
    public bool ShouldZoomXAxis(bool ctrlPressed, bool shiftPressed, bool pointerOverXAxisScrollZone)
    {
        var wheelZoomsX = ctrlPressed && !shiftPressed;
        return wheelZoomsX || pointerOverXAxisScrollZone;
    }

    public double GetZoomFactor(int delta) => delta > 0 ? 0.9 : 1.1;
}
