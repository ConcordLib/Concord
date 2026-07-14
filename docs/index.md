---
_layout: landing
description: Concord lets .NET mods patch methods while an app runs without changing the target assembly on disk.
---

<section class="cc-home-hero" aria-labelledby="cc-home-title">
  <div class="cc-home-intro">
    <h1 id="cc-home-title" class="cc-home-wordmark">
      <img src="/assets/logo.png" alt="Concord" width="480" height="152">
    </h1>
    <p class="cc-home-lead">Concord lets a .NET mod patch methods while the app runs. You write the patch in C#, and Concord leaves the target assembly on disk alone.</p>
    <div class="cc-home-actions">
      <a class="cc-btn cc-btn-primary" href="docs/introduction.md">Start here</a>
      <a class="cc-btn cc-btn-secondary" href="api/Concord.yml">API reference</a>
    </div>
  </div>

  <div class="cc-code-preview cc-home-example">
    <div class="header">
      <span class="filename">CampfireWarmthPatch.cs</span>
      <span class="cc-code-language">C#</span>
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
</section>

<section class="cc-home-section cc-runtime-demo" aria-labelledby="cc-runtime-title">
  <header class="cc-home-section-heading">
    <h2 id="cc-runtime-title">What a patch does</h2>
    <p>The example patch adds 5 to <code>Campfire.GetWarmth()</code>. Concord does not rewrite the method in the target assembly.</p>
  </header>

  <div class="cc-runtime-stage">
    <div class="cc-runtime-stage-copy">
      <h3>In the target assembly</h3>
      <p><code>GetWarmth()</code> returns twice the amount of fuel.</p>
    </div>
    <div class="cc-code-preview">
      <div class="header">
        <span class="filename">Campfire.cs</span>
        <span class="cc-code-language">C#</span>
      </div>
      <pre><code class="lang-csharp">public int GetWarmth()
{
    return fuel * 2;
}</code></pre>
    </div>
  </div>

  <div class="cc-runtime-stage">
    <div class="cc-runtime-stage-copy">
      <h3>At runtime</h3>
      <p>Concord sends each call through a wrapper that adds 5.</p>
    </div>
    <div class="cc-code-preview">
      <div class="header">
        <span class="filename">GetWarmth at runtime</span>
        <span class="cc-code-language">C#</span>
      </div>
      <pre><code class="lang-csharp">public int GetWarmth()
{
    int result = fuel * 2;
    result += 5;
    return result;
}</code></pre>
    </div>
  </div>
</section>

<section class="cc-home-section cc-home-guides" aria-labelledby="cc-guides-title">
  <header class="cc-home-section-heading">
    <h2 id="cc-guides-title">Reading order</h2>
    <p>If this is your first Concord patch, read Start Here, Getting started, and Your first patch in order. Use the rest as references.</p>
  </header>

  <div class="cc-guide-groups">
    <section class="cc-guide-group" aria-labelledby="cc-write-patches-title">
      <h3 id="cc-write-patches-title">Make a patch</h3>
      <a class="cc-guide-link" href="docs/getting-started.md">
        <strong>Getting started</strong>
        <span>Add <code>Concord.Ref</code>. Pick the optional build tools you want.</span>
      </a>
      <a class="cc-guide-link" href="docs/first-patch.md">
        <strong>Your first patch</strong>
        <span>Write and apply patches to the example <code>Campfire</code> class.</span>
      </a>
      <a class="cc-guide-link" href="docs/common-tasks.md">
        <strong>Common tasks</strong>
        <span>Find an example for each supported patch position.</span>
      </a>
    </section>
    <section class="cc-guide-group" aria-labelledby="cc-understand-title">
      <h3 id="cc-understand-title">Look it up</h3>
      <a class="cc-guide-link" href="docs/how-patches-work.md">
        <strong>How patches work</strong>
        <span>See how Concord builds a wrapper and installs the detour.</span>
      </a>
      <a class="cc-guide-link" href="docs/packages.md">
        <strong>Packages</strong>
        <span>See what each NuGet package contains and whether you need it.</span>
      </a>
      <a class="cc-guide-link" href="docs/troubleshooting.md">
        <strong>Troubleshooting</strong>
        <span>Look up an error or inspect the IL Concord generated.</span>
      </a>
      <a class="cc-guide-link" href="docs/roadmap.md">
        <strong>Roadmap</strong>
        <span>Check what Concord does not support yet.</span>
      </a>
    </section>
  </div>
</section>
