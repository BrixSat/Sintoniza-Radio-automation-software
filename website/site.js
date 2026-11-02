// Sintoniza website: the language switch (remembered in this browser) and the screenshot viewer.
(function () {
  var root = document.documentElement;
  var KEY = "sintoniza-lang";
  var titles = {
    en: "Sintoniza: radio automation for FM and web radio",
    pt: "Sintoniza: automação de rádio para FM e web rádio"
  };
  var descs = {
    en: "Free radio automation for FM and web radio stations on air 24/7: on-air playout, production, music import and the broadcast recorder. Windows, macOS and Linux.",
    pt: "Automação de rádio gratuita para rádios FM e web no ar 24 horas por dia: emissão, produção, importação de música e gravação da emissão. Windows, macOS e Linux."
  };

  function setLang(lang, save) {
    if (lang !== "pt") lang = "en";
    root.setAttribute("data-lang", lang);
    root.lang = lang === "pt" ? "pt-PT" : "en";
    document.title = titles[lang];
    var d = document.querySelector('meta[name="description"]');
    if (d) d.setAttribute("content", descs[lang]);
    document.querySelectorAll(".langs button").forEach(function (b) {
      b.setAttribute("aria-pressed", b.getAttribute("data-set-lang") === lang ? "true" : "false");
    });
    // Screenshots: the Portuguese ones on the Portuguese page.
    document.querySelectorAll("img[data-shot]").forEach(function (img) {
      var src = "img/" + lang + "/" + img.getAttribute("data-shot") + ".webp";
      if (img.getAttribute("src") !== src) img.setAttribute("src", src);
      var alt = img.getAttribute("data-alt-" + lang);
      if (alt) img.alt = alt;
    });
    if (save) {
      try { localStorage.setItem(KEY, lang); } catch (e) { /* storage blocked: the choice lasts this visit */ }
    }
  }

  var saved = null;
  try { saved = localStorage.getItem(KEY); } catch (e) { saved = null; }
  setLang(saved || "en", false);

  document.querySelectorAll(".langs button").forEach(function (b) {
    b.addEventListener("click", function () { setLang(b.getAttribute("data-set-lang"), true); });
  });

  // Screenshot viewer.
  var box = document.getElementById("lightbox");
  if (box && typeof box.showModal === "function") {
    var big = box.querySelector("img");
    var cap = box.querySelector("p");
    document.querySelectorAll(".shot button").forEach(function (btn) {
      btn.addEventListener("click", function () {
        var img = btn.querySelector("img");
        var lang = root.getAttribute("data-lang") || "en";
        var fc = btn.parentNode.querySelector('figcaption [lang="' + lang + '"]');
        big.src = img.currentSrc || img.src;
        big.alt = img.alt;
        cap.textContent = fc ? fc.textContent.replace(/\s+/g, " ").trim() : "";
        box.showModal();
      });
    });
    box.addEventListener("click", function (e) { if (e.target === box || e.target.closest(".close")) box.close(); });
  } else {
    // No dialog support: open the picture itself.
    document.querySelectorAll(".shot button").forEach(function (btn) {
      btn.addEventListener("click", function () { window.open(btn.querySelector("img").src, "_blank", "noopener"); });
    });
  }
})();
