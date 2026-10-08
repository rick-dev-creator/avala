using Avala.Agents.Contracts.Sessions;

namespace Avala.Canvas.Contracts;

public interface ICanvases
{
    IReadOnlyList<CanvasSnapshot> InSession(SessionId session);
}
