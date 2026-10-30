using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BTSMTL.Replay.Input
{
    public readonly struct CharacterInputKeyChange
    {
        public CharacterInputKeyChange(float time, int key, in CharacterInputKeyState state)
        {
            Time = time;
            Key = key;
            State = state;
        }

        public float Time { get; }
        public int Key { get; }
        public CharacterInputKeyState State { get; }
    }

    public readonly struct CharacterInputPlanPoint
    {
        public CharacterInputPlanPoint(double seconds, CharacterInputValue value, bool request)
        {
            Seconds = seconds;
            Value = value;
            Request = request;
        }

        public double Seconds { get; }
        public CharacterInputValue Value { get; }
        public bool Request { get; }
    }

    public static class CharacterInputPlan
    {
        public static IReadOnlyList<CharacterInputPlanPoint> Read(CharacterInputKeyLayout keys,
            IReadOnlyList<CharacterInputKeyChange> changes)
        {
            var points = new List<CharacterInputPlanPoint>();
            var states = new CharacterInputKeyState[keys.Count];
            var requests = new CharacterInputRequestEvaluator(keys);
            var previousRepeats = new bool[keys.RequestCount];
            for (int index = 0; index < changes.Count || !double.IsPositiveInfinity(requests.NextChangeTime);)
            {
                double time = index < changes.Count
                    ? Math.Min(changes[index].Time, requests.NextChangeTime)
                    : requests.NextChangeTime;
                for (int key = 0; key < states.Length; key++)
                    states[key] = new CharacterInputKeyState(states[key].Pressed, false, states[key].Scalar, states[key].Vector2);
                while (index < changes.Count && changes[index].Time == time)
                {
                    CharacterInputKeyChange change = changes[index++];
                    CharacterInputKeyState previous = states[change.Key];
                    states[change.Key] = new CharacterInputKeyState(change.State.Pressed,
                        previous.PressedThisFrame || change.State.Pressed && !previous.Pressed,
                        change.State.Scalar, change.State.Vector2,
                        previous.ReleasedThisFrame || !change.State.Pressed && previous.Pressed);
                }
                requests.Capture(states, time);
                for (int value = 0; value < keys.ValueCount; value++)
                    points.Add(new CharacterInputPlanPoint((float)time,
                        requests.Value(value, in states[keys.ValueKey(value)]), false));
                for (int request = 0; request < keys.RequestCount; request++)
                {
                    bool requested = requests.Requested(request);
                    CharacterInputKeyRequest definition = keys.RequestDefinition(request);
                    if (definition.RepeatWhileHeld ? requested != previousRepeats[request] : requested)
                        points.Add(new CharacterInputPlanPoint((float)time,
                            CharacterInputValue.Bool(definition.RequestId, requested), true));
                    previousRepeats[request] = requested;
                }
            }
            return points;
        }

        public static IReadOnlyList<CharacterInputPlanPoint> Schedule(CharacterInputKeyLayout keys,
            IReadOnlyList<CharacterInputPlanPoint> points, double rangeEnd, int tickRate)
        {
            var result = new List<CharacterInputPlanPoint>();
            var requests = keys.Requests.ToDictionary(request => request.RequestId, StringComparer.Ordinal);
            var values = keys.Values.ToDictionary(value => value.InputValueId, StringComparer.Ordinal);
            foreach (CharacterInputPlanPoint point in points)
            {
                if (!double.IsFinite(point.Seconds) || point.Seconds < 0d)
                    throw new InvalidDataException("输入日程时刻必须是大于等于零的有限秒数。");
                string inputId = point.Value.InputValueId;
                if (point.Request ? !requests.ContainsKey(inputId) : !values.ContainsKey(inputId))
                    throw new InvalidDataException($"输入日程中的 '{inputId}' 不属于所选角色的正式 InputProfile。");
                if (point.Seconds > rangeEnd)
                    continue;
                if (!point.Request)
                {
                    result.Add(point);
                    continue;
                }
                if (!point.Value.BoolValue)
                    continue;
                result.Add(point);
                if (!requests[inputId].RepeatWhileHeld)
                    continue;
                double end = rangeEnd;
                foreach (CharacterInputPlanPoint next in points)
                    if (next.Value.InputValueId == inputId && next.Seconds > point.Seconds)
                        end = Math.Min(end, next.Seconds);
                for (int tick = 1; point.Seconds + tick / (double)tickRate < end; tick++)
                    result.Add(new CharacterInputPlanPoint(point.Seconds + tick / (double)tickRate, point.Value, true));
            }
            return result.OrderBy(value => value.Seconds).ToArray();
        }
    }
}
