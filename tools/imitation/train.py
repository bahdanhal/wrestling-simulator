"""Train a physics controller to imitate a recorded two-person take (PPO), and export the result.

    python train.py --take ../../data/motion/takes/<id>.json --iterations 2000 --out runs/<name>
    python train.py --take <take.json> --export runs/<name>/policy.pt --out-take <data>/motion/takes

Training on a laptop CPU is only good for checking that everything runs; a real throw needs hours
to days of training on a GPU machine (pass --device cuda there).
"""
from __future__ import annotations

import argparse
import json
import os
import time

import numpy as np
import torch
from torch import nn

from env import WrestlingImitation
from reference import CONTROL_HZ, fit, fit_error, to_take


class Policy(nn.Module):
    def __init__(self, observations: int, actions: int, hidden: int = 512):
        super().__init__()
        self.actor = nn.Sequential(nn.Linear(observations, hidden), nn.ELU(), nn.Linear(hidden, hidden // 2), nn.ELU(), nn.Linear(hidden // 2, actions))
        self.critic = nn.Sequential(nn.Linear(observations, hidden), nn.ELU(), nn.Linear(hidden, hidden // 2), nn.ELU(), nn.Linear(hidden // 2, 1))
        self.log_std = nn.Parameter(torch.full((actions,), -1.0))

    def distribution(self, obs):
        return torch.distributions.Normal(self.actor(obs), self.log_std.exp())


def train(args):
    device = torch.device(args.device)
    reference = fit(args.take)
    print(f"reference {reference.take_id}: {reference.frames} frames, fit error {fit_error(reference):.3f} m")
    envs = [WrestlingImitation(reference, seed=i) for i in range(args.envs)]
    policy = Policy(envs[0].observation_size, envs[0].action_size).to(device)
    optimiser = torch.optim.Adam(policy.parameters(), lr=args.lr)
    obs = np.stack([e.reset() for e in envs])
    os.makedirs(args.out, exist_ok=True)
    log = open(os.path.join(args.out, "metrics.jsonl"), "a", encoding="utf-8")
    started = time.time()
    for iteration in range(1, args.iterations + 1):
        buffers = {k: [] for k in ("obs", "act", "logp", "rew", "done", "val")}
        errors = []
        for _ in range(args.horizon):
            with torch.no_grad():
                o = torch.as_tensor(obs, device=device)
                dist = policy.distribution(o)
                act = dist.sample()
                logp = dist.log_prob(act).sum(-1)
                val = policy.critic(o).squeeze(-1)
            actions = act.cpu().numpy()
            next_obs, rewards, dones = [], [], []
            for i, env in enumerate(envs):
                o2, r, d, info = env.step(actions[i])
                errors.append(info["keypoint_error"])
                if d:
                    o2 = env.reset()
                next_obs.append(o2)
                rewards.append(r)
                dones.append(d)
            for k, v in (("obs", obs), ("act", actions), ("logp", logp.cpu().numpy()), ("rew", rewards), ("done", dones), ("val", val.cpu().numpy())):
                buffers[k].append(np.asarray(v))
            obs = np.stack(next_obs)
        with torch.no_grad():
            last_val = policy.critic(torch.as_tensor(obs, device=device)).squeeze(-1).cpu().numpy()
        rew = np.stack(buffers["rew"]).astype(np.float32)
        done = np.stack(buffers["done"]).astype(np.float32)
        val = np.stack(buffers["val"]).astype(np.float32)
        adv = np.zeros_like(rew)
        gae = 0.0
        for t in reversed(range(args.horizon)):
            nxt = last_val if t == args.horizon - 1 else val[t + 1]
            delta = rew[t] + args.gamma * nxt * (1 - done[t]) - val[t]
            gae = delta + args.gamma * args.lam * (1 - done[t]) * gae
            adv[t] = gae
        ret = adv + val
        flat = lambda x: torch.as_tensor(np.concatenate(x) if isinstance(x, list) else x.reshape(-1, *x.shape[2:]), device=device)
        b_obs = flat(np.stack(buffers["obs"]))
        b_act = flat(np.stack(buffers["act"]))
        b_logp = flat(np.stack(buffers["logp"]))
        b_adv = flat(adv)
        b_ret = flat(ret)
        b_adv = (b_adv - b_adv.mean()) / (b_adv.std() + 1e-8)
        count = b_obs.shape[0]
        for _ in range(args.epochs):
            order = torch.randperm(count, device=device)
            for start in range(0, count, args.batch):
                idx = order[start:start + args.batch]
                dist = policy.distribution(b_obs[idx])
                logp = dist.log_prob(b_act[idx]).sum(-1)
                ratio = (logp - b_logp[idx]).exp()
                clipped = torch.clamp(ratio, 1 - args.clip, 1 + args.clip)
                policy_loss = -torch.min(ratio * b_adv[idx], clipped * b_adv[idx]).mean()
                value_loss = 0.5 * (policy.critic(b_obs[idx]).squeeze(-1) - b_ret[idx]).pow(2).mean()
                loss = policy_loss + value_loss - 0.0 * dist.entropy().mean()
                optimiser.zero_grad()
                loss.backward()
                nn.utils.clip_grad_norm_(policy.parameters(), 1.0)
                optimiser.step()
        record = {"iteration": iteration, "reward": float(rew.mean()), "keypoint_error": float(np.mean(errors)), "seconds": round(time.time() - started, 1)}
        log.write(json.dumps(record) + "\n")
        log.flush()
        if iteration % args.report == 0 or iteration == 1:
            print(f"iter {iteration:5d}  reward {record['reward']:.3f}  keypoint error {record['keypoint_error']:.3f} m  {record['seconds']:.0f}s")
        if iteration % args.save == 0 or iteration == args.iterations:
            torch.save({"policy": policy.state_dict(), "take": args.take}, os.path.join(args.out, "policy.pt"))


def export(args):
    """Roll the trained controller out from the first frame and write the simulated motion as a take."""
    reference = fit(args.take)
    env = WrestlingImitation(reference)
    policy = Policy(env.observation_size, env.action_size)
    policy.load_state_dict(torch.load(args.export, map_location="cpu")["policy"])
    obs = env.reset(frame=0)
    frames = [env.keypoints()]
    done = False
    while not done:
        with torch.no_grad():
            action = policy.actor(torch.as_tensor(obs)).numpy()
        obs, _, done, info = env.step(action)
        frames.append(env.keypoints())
    with open(args.take, encoding="utf-8") as handle:
        source = json.load(handle)
    simulated = to_take(np.stack(frames)).reshape(len(frames), 2, -1)
    take = {
        "schemaVersion": 1,
        "id": f"{source['id']}.physics",
        "source": {
            "title": f"Physics imitation of {source['id']}",
            "url": source["source"]["url"],
            "author": source["source"]["author"],
            "license": source["source"]["license"],
            "notes": f"Simulated by a trained controller ({args.export}); derived from the original take.",
        },
        "framesPerSecond": CONTROL_HZ,
        "actors": source["actors"],
        "frames": [[list(map(float, actor)) for actor in frame] for frame in simulated],
    }
    os.makedirs(args.out_take, exist_ok=True)
    path = os.path.join(args.out_take, take["id"] + ".json")
    with open(path, "w", encoding="utf-8") as handle:
        json.dump(take, handle)
        handle.write("\n")
    print(f"wrote {path}: {len(frames)} frames, last keypoint error {info['keypoint_error']:.3f} m")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--take", required=True)
    parser.add_argument("--out", default="runs/latest")
    parser.add_argument("--iterations", type=int, default=2000)
    parser.add_argument("--envs", type=int, default=16)
    parser.add_argument("--horizon", type=int, default=64)
    parser.add_argument("--epochs", type=int, default=5)
    parser.add_argument("--batch", type=int, default=256)
    parser.add_argument("--lr", type=float, default=3e-4)
    parser.add_argument("--gamma", type=float, default=0.95)
    parser.add_argument("--lam", type=float, default=0.95)
    parser.add_argument("--clip", type=float, default=0.2)
    parser.add_argument("--report", type=int, default=10)
    parser.add_argument("--save", type=int, default=50)
    parser.add_argument("--device", default="cpu")
    parser.add_argument("--export")
    parser.add_argument("--out-take")
    args = parser.parse_args()
    if args.export:
        if not args.out_take:
            parser.error("--export needs --out-take")
        export(args)
    else:
        train(args)


if __name__ == "__main__":
    main()
