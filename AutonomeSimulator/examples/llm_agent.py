"""
LLM-powered controller that uses Claude or OpenAI to make contextual decisions
about which action an NPC should take each tick.

Usage:
    # With Anthropic Claude:
    ANTHROPIC_API_KEY=sk-... python examples/llm_agent.py [entity_id]

    # With OpenAI:
    OPENAI_API_KEY=sk-... python examples/llm_agent.py [entity_id]

Requires the C# simulation server running on localhost:3801:
    dotnet run --project src/Autonome.Web -- worlds/coastal_city
"""
import sys
import os
import json
import urllib.request
import urllib.error

BASE = "http://localhost:3801"
TICKS = 50


def api(method, path, body=None, token=None):
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body else None
    req = urllib.request.Request(BASE + path, data=data, headers=headers, method=method)
    try:
        with urllib.request.urlopen(req) as resp:
            return json.loads(resp.read())
    except urllib.error.HTTPError as e:
        err = json.loads(e.read()) if e.fp else {}
        raise RuntimeError(f"HTTP {e.code}: {err.get('error', e.reason)}")


def call_anthropic(system_prompt, user_prompt, api_key):
    """Call Claude API and return the text response."""
    data = json.dumps({
        "model": "claude-sonnet-4-20250514",
        "max_tokens": 200,
        "system": system_prompt,
        "messages": [{"role": "user", "content": user_prompt}],
    }).encode()
    req = urllib.request.Request(
        "https://api.anthropic.com/v1/messages",
        data=data,
        headers={
            "Content-Type": "application/json",
            "x-api-key": api_key,
            "anthropic-version": "2023-06-01",
        },
        method="POST",
    )
    with urllib.request.urlopen(req) as resp:
        result = json.loads(resp.read())
    return result["content"][0]["text"]


def call_openai(system_prompt, user_prompt, api_key):
    """Call OpenAI API and return the text response."""
    data = json.dumps({
        "model": "gpt-4o-mini",
        "max_tokens": 200,
        "messages": [
            {"role": "system", "content": system_prompt},
            {"role": "user", "content": user_prompt},
        ],
    }).encode()
    req = urllib.request.Request(
        "https://api.openai.com/v1/chat/completions",
        data=data,
        headers={
            "Content-Type": "application/json",
            "Authorization": f"Bearer {api_key}",
        },
        method="POST",
    )
    with urllib.request.urlopen(req) as resp:
        result = json.loads(resp.read())
    return result["choices"][0]["message"]["content"]


def get_llm_caller():
    """Detect which API key is available and return the appropriate caller."""
    anthropic_key = os.environ.get("ANTHROPIC_API_KEY")
    openai_key = os.environ.get("OPENAI_API_KEY")
    if anthropic_key:
        print("Using Anthropic Claude API")
        return lambda sys, usr: call_anthropic(sys, usr, anthropic_key)
    elif openai_key:
        print("Using OpenAI API")
        return lambda sys, usr: call_openai(sys, usr, openai_key)
    else:
        print("ERROR: Set ANTHROPIC_API_KEY or OPENAI_API_KEY environment variable")
        sys.exit(1)


def build_system_prompt(entity_state):
    """Build a system prompt describing the entity and world context."""
    name = entity_state.get("displayName", entity_state["id"])
    loc = entity_state.get("location", "unknown")
    identity = entity_state.get("identity", {})
    role = identity.get("role", "inhabitant")
    tags = identity.get("tags", [])

    props_text = ""
    for prop, val in entity_state.get("properties", {}).items():
        v = val["value"] if isinstance(val, dict) else val
        props_text += f"  {prop}: {v:.2f}\n"

    return f"""You are {name}, a {role} in a medieval coastal city simulation.
Your location: {loc}
Your tags: {', '.join(tags) if tags else 'none'}

Your current properties:
{props_text}
You must respond with ONLY the action ID to take. No explanation, no punctuation, just the action ID string.
Choose actions that serve your role and address your most pressing needs (low properties).
Prioritize survival needs (hunger, rest) when critical, then social/economic goals."""


def build_user_prompt(actions, recent_events):
    """Build the user prompt with available actions and recent context."""
    acts_text = ""
    for a in actions:
        acts_text += f"  {a['actionId']} (score: {a['score']:.3f}) - {a.get('displayName', a['actionId'])}\n"

    events_text = ""
    for ev in recent_events[-5:]:
        events_text += f"  [{ev.get('tick', '?')}] {ev.get('actionId', '?')} @ {ev.get('location', '?')}\n"

    return f"""Available actions (sorted by AI utility score):
{acts_text}
Your recent actions:
{events_text if events_text else '  (none yet)'}

Which action do you choose? Reply with ONLY the action ID."""


def main():
    llm_call = get_llm_caller()
    entity_id = sys.argv[1] if len(sys.argv) > 1 else None

    if not entity_id:
        entities = api("GET", "/api/world/entities")
        embodied = [e for e in entities if e["embodied"]]
        entity_id = embodied[0]["id"]
        print(f"No entity specified, using: {entity_id}")

    # Possess
    slot = api("POST", "/api/entity/possess", {"entityId": entity_id})
    token = slot["token"]
    print(f"Possessed {entity_id} at {slot['location']}")

    recent_actions = []
    try:
        for i in range(TICKS):
            # Get state and actions
            state = api("GET", f"/api/entity/{entity_id}/state")
            acts_resp = api("GET", f"/api/entity/{entity_id}/actions")
            actions = acts_resp.get("actions", [])

            if not actions:
                api("POST", "/api/simulation/tick")
                print(f"[Tick {i}] No actions available (busy or no candidates)")
                continue

            action_ids = [a["actionId"] for a in actions]

            # Ask the LLM
            sys_prompt = build_system_prompt(state)
            usr_prompt = build_user_prompt(actions, recent_actions)

            try:
                choice = llm_call(sys_prompt, usr_prompt).strip()
                # Validate the choice
                if choice not in action_ids:
                    # Try fuzzy match
                    matches = [a for a in action_ids if choice.lower() in a.lower()]
                    choice = matches[0] if matches else actions[0]["actionId"]
                    print(f"  (LLM response adjusted to: {choice})")
            except Exception as e:
                choice = actions[0]["actionId"]
                print(f"  (LLM error: {e}, falling back to: {choice})")

            # Submit action
            api("POST", f"/api/entity/{entity_id}/act",
                {"actionId": choice}, token=token)

            # Advance tick
            result = api("POST", "/api/simulation/tick")
            my_events = [e for e in result.get("events", [])
                         if e["autonomeId"] == entity_id]

            if my_events:
                ev = my_events[0]
                recent_actions.append(ev)
                if len(recent_actions) > 10:
                    recent_actions.pop(0)
                print(f"[Tick {result['tick']:3d}] LLM chose: {choice:<24s} @ {ev['location']}")
            else:
                print(f"[Tick {result['tick']:3d}] LLM chose: {choice:<24s} (busy/skipped)")

        # Final summary
        final = api("GET", f"/api/entity/{entity_id}/state")
        print(f"\n--- Final State ---")
        print(f"Location: {final['location']}")
        for prop, val in final["properties"].items():
            v = val["value"] if isinstance(val, dict) else val
            print(f"  {prop:<20s} {v:.3f}")

    finally:
        api("POST", "/api/entity/release", {"entityId": entity_id})
        print(f"\nReleased {entity_id}")


if __name__ == "__main__":
    main()
