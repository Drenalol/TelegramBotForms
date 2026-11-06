using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Drenalol.Telegram.Bot.Forms;

/// <summary>A single conversation step of a stateful form.</summary>
public delegate Task<MessageContext> RenderAction(MessageContext context, CancellationToken cancellationToken);

/// <summary>A render action, optionally carrying the message shown when the state is entered first.</summary>
public interface IRender
{
  public RenderAction Action { get; }
}

/// <summary>
/// Base class for stateful (wizard-like) forms: the conversation is a set of named
/// states (<typeparamref name="TState"/>, so any application enum works), each with a
/// registered render action. The first render initialises the form
/// (<see cref="RenderInitAsync"/>); later renders route the incoming text to a state.
/// Navigating to another form is available through <c>NavigateToAsync</c> on
/// <see cref="FormBase"/>.
/// </summary>
public abstract class ExtendedFormBase<TState> : FormBase, IStatefulForm where TState : Enum, new()
{
  protected TState? State { get; set; }

  protected IDictionary<TState, IRender> Renders
  {
    get
    {
      if (_renders.Count == 0)
        throw new InvalidOperationException($"No render actions added to {GetType().Name}");

      return _renders;
    }
  }

  public bool IsCompleted { get; protected set; }

  private int _isInited;
  private readonly IDictionary<TState, IRender> _renders = new Dictionary<TState, IRender>();

  public void Reset() => _isInited = 0;

  /// <summary>
  /// Writes the conversation position into the persistence envelope: the current
  /// state (by enum name), the completion and init flags, and - through
  /// <see cref="FormBase.CaptureContext"/> - whatever data the form itself declares.
  /// </summary>
  protected internal override string? CaptureState()
  {
    var envelope = new JsonObject
    {
      ["state"] = State?.ToString(),
      ["completed"] = IsCompleted,
      ["inited"] = Volatile.Read(ref _isInited) == 1,
    };

    CaptureFields(envelope);

    envelope["data"] = JsonSerializer.SerializeToNode(CaptureContext());

    return envelope.ToJsonString();
  }

  /// <summary>
  /// Re-applies a persisted position to a fresh instance: a renamed state value or a
  /// context that no longer deserializes throws, and the entry is skipped by the
  /// restore rather than corrupting the history.
  /// </summary>
  protected internal override void RestoreState(string? state)
  {
    if (string.IsNullOrEmpty(state))
      return;

    var envelope = JsonNode.Parse(state)!.AsObject();

    if (envelope["state"] is JsonValue stateValue && stateValue.TryGetValue<string>(out var stateName))
      State = (TState)Enum.Parse(typeof(TState), stateName, ignoreCase: true);

    IsCompleted = ReadBool(envelope, "completed");

    ApplyFields(envelope);

    if (envelope["data"] is { } data && data.GetValueKind() != JsonValueKind.Null)
      RestoreContext(data);

    // a form restored mid-conversation must not run its init render again
    if (ReadBool(envelope, "inited"))
      Interlocked.Exchange(ref _isInited, 1);
  }

  /// <summary>Adds this form's own framework fields to the persistence envelope.</summary>
  protected virtual void CaptureFields(JsonObject envelope)
  {
  }

  /// <summary>Re-applies the framework fields written by <see cref="CaptureFields"/>.</summary>
  protected virtual void ApplyFields(JsonObject envelope)
  {
  }

  private static bool ReadBool(JsonObject envelope, string name) =>
    envelope[name] is JsonValue value && value.TryGetValue<bool>(out var flag) && flag;

  public override Task<MessageContext> RenderAsync(MessageContext context, CancellationToken cancellationToken)
  {
    if (Interlocked.CompareExchange(ref _isInited, 1, 0) != 1)
    {
      if (TryGetRender(context, out var render) && render is InitRender initRender)
        context = context.Reply(initRender.InitMessage);

      return RenderInitAsync(context, cancellationToken);
    }

    return GetRender(context).Action(context, cancellationToken);
  }

  /// <summary>Finds the render action the incoming text routes to.</summary>
  protected abstract IRender GetRender(MessageContext context);

  /// <summary>The very first render of this form instance (entry point).</summary>
  protected virtual Task<MessageContext> RenderInitAsync(MessageContext context, CancellationToken cancellationToken) => Task.FromResult(context);

  /// <summary>Registers the action for <paramref name="state"/>; <paramref name="replyText"/> is replayed once when the state is entered as the form's first render.</summary>
  protected void AddRenderOn(TState state, string replyText, RenderAction action)
  {
    var render = new InitRender(replyText, action);

    _renders.Add(state, render);
  }

  /// <summary>Routes <paramref name="state"/> to another form (resolved and rendered from DI).</summary>
  protected void AddFormRenderOn<TForm>(TState state) where TForm : FormBase => _renders.Add(state, new Render(NavigateToAsync<TForm>));

  /// <summary>A render action without navigation (re-renders the current form with updated state).</summary>
  protected static IRender CustomRender(RenderAction action) => new Render(action);

  private bool TryGetRender(MessageContext context, [NotNullWhen(true)] out IRender? render)
  {
    try
    {
      render = GetRender(context);
      return true;
    }
    catch
    {
      render = null;
      return false;
    }
  }

  private record InitRender(string InitMessage, RenderAction Action) : IRender;

  private record Render(RenderAction Action) : IRender;
}
