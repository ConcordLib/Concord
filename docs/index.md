---
_layout: landing
---

<div align="center">
  <img src="./assets/logo.png" alt="Concord" width="480" style="margin-top: 2rem;">
</div>

Concord lets a mod change target runtime behavior while it is running, without touching runtime files.

<div class="cc-hero-cta" style="margin-top: 2rem;">
  <a class="cc-btn cc-btn-primary" href="docs/introduction.html">Get Started →</a>
  <a class="cc-btn cc-btn-secondary" href="api/Concord.html">API Reference</a>
</div>

<h2 class="cc-section-title">Explore</h2>

<div class="cc-features">
  <a class="cc-feature-card" href="docs/introduction.html">
    <h3>Start Here</h3>
    <p>The core ideas: patches, targets, control handles, injections, and private target fields.</p>
    <span class="arrow">Read guide →</span>
  </a>
  <a class="cc-feature-card" href="docs/first-patch.html">
    <h3>Your First Patch</h3>
    <p>A guided walk-through from target code to applied injection.</p>
    <span class="arrow">Try it →</span>
  </a>
  <a class="cc-feature-card" href="docs/common-tasks.html">
    <h3>Common Tasks</h3>
    <p>The patches you'll write most: head, return, around, cancel, reverse.</p>
    <span class="arrow">Browse tasks →</span>
  </a>
  <a class="cc-feature-card" href="docs/how-patches-work.html">
    <h3>How Patches Work</h3>
    <p>How it works underneath: wrappers, detours, original bodies, and the apply flow.</p>
    <span class="arrow">Understand →</span>
  </a>
  <a class="cc-feature-card" href="docs/roadmap.html">
    <h3>Roadmap</h3>
    <p>Current support and planned work: more injection positions, shared state, wider attached data, and enum patching.</p>
    <span class="arrow">Look ahead →</span>
  </a>
  <a class="cc-feature-card" href="docs/packages.html">
    <h3>Packages</h3>
    <p>What each project is and which one you need: ref vs runtime vs a runtime adapter.</p>
    <span class="arrow">Sort it out →</span>
  </a>
</div>

<div class="cc-code-preview">
  <div class="header">
    <span class="dot red"></span>
    <span class="dot yellow"></span>
    <span class="dot green"></span>
    <span class="filename">CampfireWarmthPatch.cs</span>
  </div>
  <pre><code class="lang-csharp">[Patch]
abstract class CampfireWarmthPatch : Campfire
{
    [Inject(At.Return, nameof(GetWarmth))]
    void AfterGetWarmth(ControlHandle&lt;int&gt; ch)
    {
        ch.ReturnValue += 5;
    }
}</code></pre>
</div>

<div class="cc-before-after">
  <div class="cc-ba-col">
    <div class="cc-ba-label">Before</div>
    <div class="cc-code-preview">
      <div class="header">
        <span class="dot red"></span>
        <span class="dot yellow"></span>
        <span class="dot green"></span>
        <span class="filename">Campfire.cs</span>
      </div>
      <pre><code class="lang-csharp">public int GetWarmth()
{
    return fuel * 2;
}</code></pre>
    </div>
  </div>
  <div class="cc-ba-arrow">→</div>
  <div class="cc-ba-col">
    <div class="cc-ba-label">After patch</div>
    <div class="cc-code-preview">
      <div class="header">
        <span class="dot red"></span>
        <span class="dot yellow"></span>
        <span class="dot green"></span>
        <span class="filename">GetWarmth (runtime)</span>
      </div>
      <pre><code class="lang-csharp">public int GetWarmth()
{
    int result = fuel * 2;
    result += 5;
    return result;
}</code></pre>
    </div>
  </div>
</div>
