"""
Simple bot that possesses an NPC, picks the highest-scored action each tick,
and logs state changes over 100 ticks.

Usage:
    python examples/python_client.py [entity_id]

Requires the C# simulation server running on localhost:3801:
    dotnet run --project src/Autonome.Web -- worlds/coastal_city
"""
import sys
import time
import json
import urllib.request
import urllib.error

BASE = "http://localhost:3801"
TICKS = 100


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


def main():
    # Pick entity from CLI arg or default
    entity_id = sys.argv[1] if len(sys.argv) > 1 else None

    # Find a suitable entity if none specified
    if not entity_id:
        entities = api("GET", "/api/world/entities")
        embodied = [e for e in entities if e["embodied"]]
        if not embodied:
            print("No embodied entities found!")
            return
        entity_id = embodied[0]["id"]
        print(f"No entity specified, using: {entity_id}")

    # Possess
    slot = api("POST", "/api/entity/possess", {"entityId": entity_id})
    token = slot["token"]
    print(f"Possessed {entity_id} at {slot['location']} (token: {token[:16]}...)")

    try:
        for i in range(TICKS):
            # Get available actions
            acts = api("GET", f"/api/entity/{entity_id}/actions")
            actions = acts.get("actions", [])

            if actions:
                best = actions[0]
                # Submit the highest-scored action
                api("POST", f"/api/entity/{entity_id}/act",
                    {"actionId": best["actionId"]}, token=token)

            # Advance one tick
            result = api("POST", "/api/simulation/tick")

            # Find our event in the tick result
            my_events = [e for e in result.get("events", [])
                         if e["autonomeId"] == entity_id]

            if my_events:
                ev = my_events[0]
                print(f"[Tick {result['tick']:3d}] {ev['actionId']:<24s} "
                      f"score={ev['score']:.3f}  @ {ev['location']}")
            else:
                # Entity is busy or was skipped
                state = api("GET", f"/api/entity/{entity_id}/state")
                busy = state.get("busyUntilTick", 0)
                if busy > result["tick"]:
                    print(f"[Tick {result['tick']:3d}] (busy until tick {busy})")
                else:
                    print(f"[Tick {result['tick']:3d}] (idle)")

        # Final state summary
        final = api("GET", f"/api/entity/{entity_id}/state")
        print(f"\n--- Final State (tick {final.get('tick', '?')}) ---")
        print(f"Location: {final['location']}")
        for prop, val in final["properties"].items():
            v = val["value"] if isinstance(val, dict) else val
            print(f"  {prop:<20s} {v:.3f}")

    finally:
        api("POST", "/api/entity/release", {"entityId": entity_id})
        print(f"\nReleased {entity_id}")


if __name__ == "__main__":
    main()
