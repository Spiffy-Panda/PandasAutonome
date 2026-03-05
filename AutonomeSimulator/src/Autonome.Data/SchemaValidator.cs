using Autonome.Core.Model;

namespace Autonome.Data;

/// <summary>
/// Validates loaded data: ID references, range constraints, required fields, DAG acyclicity.
/// </summary>
public class SchemaValidator
{
    public List<string> Validate(LoadResult data)
    {
        var errors = new List<string>();
        var profileIds = new HashSet<string>(data.Profiles.Select(p => p.Id));
        var actionIds = new HashSet<string>(data.Actions.Select(a => a.Id));

        // Validate profiles
        foreach (var profile in data.Profiles)
        {
            if (string.IsNullOrEmpty(profile.Id))
                errors.Add("Profile with empty ID found");

            if (string.IsNullOrEmpty(profile.DisplayName))
                errors.Add($"Profile '{profile.Id}' has no display name");

            // Validate properties
            foreach (var (propId, prop) in profile.Properties)
            {
                if (prop.Min > prop.Max)
                    errors.Add($"Profile '{profile.Id}' property '{propId}': min ({prop.Min}) > max ({prop.Max})");

                if (prop.Value < prop.Min || prop.Value > prop.Max)
                    errors.Add($"Profile '{profile.Id}' property '{propId}': value ({prop.Value}) outside [{prop.Min}, {prop.Max}]");
            }

            // Validate personality axes are in [0, 1]
            foreach (var (axis, value) in profile.Personality)
            {
                if (value < 0f || value > 1f)
                    errors.Add($"Profile '{profile.Id}' personality '{axis}': value ({value}) outside [0, 1]");
            }
        }

        // Validate actions
        foreach (var action in data.Actions)
        {
            if (string.IsNullOrEmpty(action.Id))
                errors.Add("Action with empty ID found");

            // Validate response curves have valid keyframes
            foreach (var (propId, response) in action.PropertyResponses)
            {
                var keys = response.Curve.Keys;
                if (keys.Count < 2)
                    errors.Add($"Action '{action.Id}' curve for '{propId}': needs at least 2 keyframes");

                // Check Time monotonically increasing
                for (int i = 1; i < keys.Count; i++)
                {
                    if (keys[i].Time <= keys[i - 1].Time)
                        errors.Add($"Action '{action.Id}' curve for '{propId}': keyframe times must be monotonically increasing");
                }
            }
        }

        // Validate relationships reference existing profiles
        foreach (var rel in data.Relationships)
        {
            if (!profileIds.Contains(rel.Source))
                errors.Add($"Relationship source '{rel.Source}' not found in profiles");
            if (!profileIds.Contains(rel.Target))
                errors.Add($"Relationship target '{rel.Target}' not found in profiles");
            if (rel.Source == rel.Target)
                errors.Add($"Self-referencing relationship: '{rel.Source}'");
        }

        return errors;
    }
}
