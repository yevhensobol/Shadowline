namespace Shadowline.Core.Reports;

public sealed record Reference(string Id, string Citation, string? Doi, string? Url, string? Note = null, string? NoteUk = null);

public static class References
{
    public static Reference De440 { get; } = new("de440", "Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H. (2021). The JPL Planetary and Lunar Ephemerides DE440 and DE441. The Astronomical Journal 161, 105.", "10.3847/1538-3881/abd414", null);

    public static Reference Spice { get; } = new("spice", "Acton, C. H. (1996). Ancillary data services of NASA's Navigation and Ancillary Information Facility. Planetary and Space Science 44, 65-70. Kernels used: de440s.bsp, moon_pa_de440_200625.bpc, moon_de440_250416.tf, naif0012.tls.", "10.1016/0032-0633(95)00107-7", "https://naif.jpl.nasa.gov/pub/naif/generic_kernels/");

    public static Reference Iau2015 { get; } = new("iau2015", "Archinal, B. A. et al. (2018). Report of the IAU Working Group on Cartographic Coordinates and Rotational Elements: 2015. Celestial Mechanics and Dynamical Astronomy 130, 22.", "10.1007/s10569-017-9805-5", null);

    public static Reference Nominal { get; } = new("iau-nominal", "Prša, A. et al. (2016). Nominal values for selected solar and planetary quantities: IAU 2015 Resolution B3. The Astronomical Journal 152, 41.", "10.3847/0004-6256/152/2/41", null);

    public static Reference Lola2010 { get; } = new("lola2010", "Smith, D. E. et al. (2010). Initial observations from the Lunar Orbiter Laser Altimeter (LOLA). Geophysical Research Letters 37, L18204.", "10.1029/2010GL043751", null);

    public static Reference Lola2017 { get; } = new("lola2017", "Smith, D. E. et al. (2017). Summary of the results from the Lunar Orbiter Laser Altimeter after seven years in lunar orbit. Icarus 283, 70-91. Global DEM ldem_16 (16 pixels per degree), NASA PDS.", "10.1016/j.icarus.2016.06.006", "https://pds-geosciences.wustl.edu/missions/lro/lola.htm");

    public static Reference Barker2021 { get; } = new("barker2021", "Barker, M. K. et al. (2021). Improved LOLA elevation maps for south pole landing sites: Error estimates and their impact on illumination conditions. Planetary and Space Science 203, 105119.", "10.1016/j.pss.2020.105119", null);

    public static Reference Barker2023 { get; } = new("barker2023", "Barker, M. K. et al. (2023). A new view of the lunar south pole from the Lunar Orbiter Laser Altimeter (LOLA). The Planetary Science Journal 4, 183. Product LDEM_80S_80MPP_ADJ, data doi:10.60903/gsfcpgda-lola-spole.", "10.3847/PSJ/acf3e1", "https://pgda.gsfc.nasa.gov/products/90");

    public static Reference Mazarico2011 { get; } = new("mazarico2011", "Mazarico, E., Neumann, G. A., Smith, D. E., Zuber, M. T., Torrence, M. H. (2011). Illumination conditions of the lunar polar regions using LOLA topography. Icarus 211, 1066-1081.", "10.1016/j.icarus.2010.10.030", null);

    public static Reference Glaser2014 { get; } = new("glaser2014", "Gläser, P., Haase, I., Oberst, J., Neumann, G. A. (2014). Connecting Ridge: a landing site at the lunar south pole with extended illumination. European Planetary Science Congress 2014, EPSC2014-136.", null, "https://elib.dlr.de/94242/1/EPSC2014-136-1.pdf", "Points on the Shackleton to de Gerlache ridge in sunlight 92.0% and 92.3% of 19 years, 2 m above the ground.", "Точки на хребті між Шеклтоном і де Жерлашем на сонці 92.0% і 92.3% часу за 19 років, 2 м над поверхнею.");

    public static Reference Alsj { get; } = new("alsj", "Scotti, J. V. Apollo Lunar Surface Journal: Earth and Sun altitude and azimuth at the Apollo landing sites. NASA History.", null, "https://www.nasa.gov/history/alsj/alsj-EarthSun_AltAz.html");

    public static Reference Wueller2026 { get; } = new("wueller2026", "Wueller, L. et al. (2026). Assessing potential landing sites with favorable illumination and accessible, potentially volatile-rich PSRs within Artemis candidate landing regions. Journal of Geophysical Research: Planets 131, e2025JE009434. Data: Zenodo record 17084058, CC BY 4.0. Work from the 2024 LPI Exploration Science Summer Intern Program, not a NASA site selection.", "10.1029/2025JE009434", "https://doi.org/10.5281/zenodo.17084058");

    public static Reference Kopp2011 { get; } = new("kopp2011", "Kopp, G., Lean, J. L. (2011). A new, lower value of total solar irradiance: Evidence and climate significance. Geophysical Research Letters 38, L01706.", "10.1029/2010GL045777", null);

    public static Reference Au2012 { get; } = new("iau2012", "IAU (2012). Resolution B2 on the re-definition of the astronomical unit of length: 1 au = 149 597 870 700 m.", null, "https://iauarchive.eso.org/static/resolutions/IAU2012_English.pdf");

    public static Reference Si2019 { get; } = new("si2019", "BIPM (2019). The International System of Units (SI), 9th edition: c = 299 792 458 m/s.", null, "https://www.bipm.org/en/publications/si-brochure");

    public static Reference Vasavada2012 { get; } = new("vasavada2012", "Vasavada, A. R. et al. (2012). Lunar equatorial surface temperatures and regolith properties from the Diviner Lunar Radiometer Experiment. Journal of Geophysical Research: Planets 117.", "10.1029/2011JE003987", null);

    public static Reference Williams2017 { get; } = new("williams2017", "Williams, J.-P., Paige, D. A., Greenhagen, B. T., Sefton-Nash, E. (2017). The global surface temperatures of the Moon as measured by the Diviner Lunar Radiometer Experiment. Icarus 283, 300-325.", "10.1016/j.icarus.2016.08.012", null);

    public static Reference Fcc2021 { get; } = new("fcc2021", "Intuitive Machines (2021). NOVA-C Lunar Lander and IM-1 Mission, Technical Appendix (Attachment A), FCC application SAT-LOA-20210423-00055: three fixed body-mounted solar panels, 788 W maximum, Li-ion batteries 1554 Wh, unregulated 28 V bus, lunar surface phase 14 days.", null, "https://fcc.report/IBFS/SAT-LOA-20210423-00055/6378380.pdf");

    public static Reference Astronomy2025 { get; } = new("astronomy2025", "Astronomy (2025). Intuitive Machines preps IM-2 mission for Moon launch: three solar panels give 200 W on the surface for one lunar day; the battery does not carry Nova-C through the lunar night.", null, "https://www.astronomy.com/space-exploration/intuitive-machines-preps-im-2-mission-for-moon-launch-this-week/");

    public static Reference Afp2024 { get; } = new("afp2024", "AFP (2024). US Moon lander's battery likely has hours left: company, 28 February 2024. Quotes the Intuitive Machines IM-1 update: battery life may continue up to an additional 10-20 hours.", null, "https://phys.org/news/2024-02-moon-lander-battery-hours-left.html");

    public static Reference FireflyM1 { get; } = new("firefly-m1", "Firefly Aerospace. Blue Ghost Mission 1: two side solar panels and one on the top deck provide up to 400 W.", null, "https://fireflyspace.com/missions/blue-ghost-mission-1/");

    public static Reference Oig2023 { get; } = new("oig2023", "NASA Office of Inspector General (2023). Audit of NASA's Deep Space Network, IG-23-016: the antennas operate at capacity and are oversubscribed.", null, "https://oig.nasa.gov/docs/IG-23-016.pdf");

    public static Reference Dsn810 { get; } = new("dsn810", "JPL (2024). DSN Telecommunications Link Design Handbook 810-005, module 301: Coverage and Geometry.", null, "https://deepspace.jpl.nasa.gov/dsndocs/810-005/");

    public static Reference Lieske1977 { get; } = new("lieske1977", "Lieske, J. H., Lederle, T., Fricke, W., Morando, B. (1977). Expressions for the precession quantities based upon the IAU (1976) system of astronomical constants. Astronomy and Astrophysics 58, 1-16.", null, "https://ui.adsabs.harvard.edu/abs/1977A%26A....58....1L");

    public static Reference Aoki1982 { get; } = new("aoki1982", "Aoki, S. et al. (1982). The new definition of Universal Time. Astronomy and Astrophysics 105, 359-361.", null, "https://ui.adsabs.harvard.edu/abs/1982A%26A...105..359A");

    public static Reference Wgs84 { get; } = new("wgs84", "NIMA (2000). Department of Defense World Geodetic System 1984, Technical Report TR8350.2, 3rd edition.", null, null);

    public static Reference Horizons { get; } = new("horizons", "Giorgini, J. D. et al. (1996). JPL's on-line Solar System data service. Bulletin of the American Astronomical Society 28, 1158.", null, "https://ssd.jpl.nasa.gov/horizons/", "Used as the validation reference.", "Еталон для перевірки.");

    public static Reference Jones1967 { get; } = new("jones1967", "Jones, R. L. (1967). An investigation of Earthshine lighting conditions for lunar-surface operations. NASA Manned Spacecraft Center.", null, "https://ntrs.nasa.gov/citations/19680001705", "Apollo mission requirements: landing in sunlight within 7° to 20° of the sunrise terminator (a planning rule; Apollo 12 landed at about 5°).", "Вимоги місій Apollo: посадка при висоті Сонця від 7° до 20° після ранкового термінатора (правило планування; Apollo 12 сів приблизно при 5°).");

    public static Reference Viper2022 { get; } = new("viper2022", "Shirley, M., Balaban, E. (2022). Overview of mission planning for the VIPER rover. NASA Ames Research Center.", null, "https://www.nasa.gov/wp-content/uploads/2022/05/overview_of_mission_planning_for_the_viper_rover.pdf", "Safe haven: no more than 50 h of darkness while Earth is below the horizon (endurance in minimum power mode 50 h), 15° rover slope limit, traverse plans tested with a start time spread of about 2 h.", "Безпечна стоянка: не більше 50 год темряви, поки Земля під горизонтом (автономність у режимі мінімального живлення 50 год), гранична крутизна схилу для ровера 15°, плани маршрутів перевірено з розкидом часу старту близько 2 год.");

    public static Reference Ochmo2023 { get; } = new("ochmo2023", "NASA Office of the Chief Health and Medical Officer (2022). Lighting considerations for Artemis, technical brief OCHMO-TB-001 Rev A.", null, "https://www.nasa.gov/wp-content/uploads/2023/03/artemis-lighting-considerations-overview-technical-brief-ochmo.pdf", "Shadows about 27 times the object height at 1.5° to 2° Sun (geometry gives 29 to 38 times).", "Довжина тіней приблизно 27 висот об'єкта при висоті Сонця 1.5°-2° (геометрично 29-38).");

    public static Reference Null2023 { get; } = new("null2023", "Null, C. et al. (2023). Identification of risks to EVA derived from lighting conditions at the lunar south pole. IAASS 2023.", null, "https://ntrs.nasa.gov/citations/20230006101", "Sun 2° to 6° at Artemis sites.", "Висота Сонця 2°-6° у місцях Artemis.");

    public static Reference Fincannon2020 { get; } = new("fincannon2020", "Fincannon, H. J. (2020). Solar power generation. NASA Glenn Research Center.", null, "https://ntrs.nasa.gov/citations/20205002844", "Table 1 solar array design factors for the lunar surface: start-of-life total 0.775 (includes temperature 0.95 and 0.93, Sun distance 0.967), end of life 0.601. Cells 28% at 50 °C and 22% at 120 °C; about 40 to 50 °C for a Sun-pointed cell at the pole; a fixed cylindrical array needs roughly 3 times the cells of a Sun-tracking one.", "Таблиця 1, проєктні коефіцієнти сонячних батарей для поверхні Місяця: на початку ресурсу разом 0.775 (включно з температурними 0.95 і 0.93 та відстанню до Сонця 0.967), наприкінці ресурсу 0.601. Елементи 28% при 50 °C і 22% при 120 °C; близько 40-50 °C для елемента, наведеного на Сонце, на полюсі; нерухомій циліндричній батареї потрібно приблизно втричі більше елементів, ніж батареї зі стеженням за Сонцем.");

    public static Reference Hayne2017 { get; } = new("hayne2017", "Hayne, P. O. et al. (2017). Global regolith thermophysical properties of the Moon from the Diviner Lunar Radiometer Experiment. Journal of Geophysical Research: Planets 122, 2371-2400.", "10.1002/2017JE005387", null, "Thermal model parameters, Table A1; open implementation heat1d.", "Параметри теплової моделі, таблиця A1; відкрита реалізація heat1d.");

    public static Reference Pedregosa2011 { get; } = new("pedregosa2011", "Pedregosa, F. et al. (2011). Scikit-learn: Machine Learning in Python. Journal of Machine Learning Research 12, 2825-2830.", null, "https://jmlr.org/papers/v12/pedregosa11a.html");

    public static Reference Paige2010 { get; } = new("paige2010", "Paige, D. A. et al. (2010). Diviner Lunar Radiometer observations of cold traps in the Moon's south polar region. Science 330, 479-482. Polar Resource Product LRO-L-DLRE-5-PRP.", "10.1126/science.1187726", null);

    public static Reference Rogers1972 { get; } = new("rogers1972", "Rogers, W. F. (1972). Apollo experience report: Lunar module landing gear subsystem. NASA TN D-6850, Manned Spacecraft Center.", null, "https://ntrs.nasa.gov/citations/19720018253", "Footpads 167.57 in (4.256 m) from the axis; touchdown design envelope: lateral speed 4 ft/s (1.22 m/s), effective slope up to 12°; Monte Carlo probability of a stable landing 0.967 to 0.998.", "Опори за 167.57 дюйма (4.256 м) від осі; проєктні умови посадки: бічна швидкість 4 фут/с (1.22 м/с), ефективний схил до 12°; імовірність стійкої посадки за методом Монте-Карло 0.967-0.998.");

    public static Reference Rogers1973 { get; } = new("rogers1973", "Rogers, W. F. (1973). Apollo lunar module landing gear. NASA Technical Reports Server 19730010151, pp. 123-134.", null, "https://ntrs.nasa.gov/citations/19730010151", "Toppling stability is governed by touchdown velocity and the slope at the touchdown point; the toppling boundary lay well within the design velocity envelope.", "Стійкість проти перекидання визначають швидкість торкання і схил у точці посадки; межа перекидання лежала значно далі за проєктні швидкості.");

    public static Reference Ali2026 { get; } = new("ali2026", "Ali, R. M., Slagle, A. C., Mordasiewicz, A., Orphee, J., McDonough, P. J. (2026). Apollo 11 lunar module touchdown dynamics reconstruction verification and validation for human landing systems. NASA Technical Reports Server 20260001760.", null, "https://ntrs.nasa.gov/citations/20260001760", "Apollo 11 LM centre of mass 3.62 m above the footpad bottom.", "Центр мас Apollo 11 LM на 3.62 м над нижньою точкою опор.");

    public static Reference Blanco2024 { get; } = new("blanco2024", "Blanco, P., Mungan, C. E. (2024). Tripping on the Moon. Physics Education 59, 043011.", "10.1088/1361-6552/ad539c", null, "Tip-over speed of a rigid lander pivoting on a caught footpad, v = sqrt(2g (I/m + r²)(r - y)/y²); IM-1 model parameters and about 2.4 m/s on level ground.", "Швидкість перекидання жорсткого апарата, що обертається навколо зачепленої опори, v = sqrt(2g (I/m + r²)(r - y)/y²); параметри моделі IM-1 і близько 2.4 м/с на рівній поверхні.");

    public static Reference Rosenburg2011 { get; } = new("rosenburg2011", "Rosenburg, M. A. et al. (2011). Global surface slopes and roughness of the Moon from the Lunar Orbiter Laser Altimeter. Journal of Geophysical Research 116, E02001.", "10.1029/2010JE003716", null, "Slopes scale with baseline as tan θ(L) = tan θ(L0)·(L/L0)^(H-1); median Hurst exponent 0.95 in the highlands, 0.76 in the maria.", "Схили змінюються з базою як tan θ(L) = tan θ(L0)·(L/L0)^(H-1); медіанний показник Херста 0.95 у високогір'ях, 0.76 у морях.");

    public static Reference BlueGhostSize { get; } = new("blueghost2025", "AmericaSpace (2025). Blue Ghost lander progresses through checkouts. Dimensions from Firefly Aerospace: 3.5 m wide, 2 m tall, four legs spanning 3 m.", null, "https://www.americaspace.com/2025/01/30/blue-ghost-lander-progresses-through-checkouts/");

    public static Reference Jaxa2024 { get; } = new("jaxa2024", "JAXA (2024). SLIM press conference, 25 January 2024, as reported by SpacePolicyOnline: SLIM landed 55 m east of the target.", null, "https://spacepolicyonline.com/news/japans-slim-lunar-lander-notches-more-successes/");

    public static Reference Firefly2025 { get; } = new("firefly2025", "Firefly Aerospace (2025). Firefly Aerospace becomes first commercial company to successfully land on the Moon, 2 March 2025: touchdown within the 100 m landing target.", null, "https://investors.fireflyspace.com/node/6786/pdf");

    public static Reference NasaApollo12 { get; } = new("nasa-apollo12", "NASA (2024). 55 years ago: Apollo 12 makes a pinpoint landing on the Moon. Intrepid landed 538 ft (164 m) from Surveyor 3.", null, "https://www.nasa.gov/history/55-years-ago-apollo-12-makes-a-pinpoint-landing-on-the-moon");

    public static Reference Im2025 { get; } = new("im2025", "Intuitive Machines (2025). Intuitive Machines executes southernmost lunar landing and operates payloads, 7 March 2025: Athena landed 250 m from the intended site. NASA reported more than 400 m.", null, "https://investors.intuitivemachines.com/news-releases/news-release-details/intuitive-machines-executes-southernmost-lunar-landing-and");

    public static Reference Spo2024 { get; } = new("spo2024", "SpacePolicyOnline (2024). NASA and IM agree Odysseus is a success: LRO located the lander about 1.5 km from its intended site on a 12° slope.", null, "https://spacepolicyonline.com/news/nasa-and-im-agree-odysseus-is-a-success/");

    public static Reference NasaApollo11 { get; } = new("nasa-apollo11", "NASA. Apollo 11 mission overview: Eagle landed about four miles downrange from the predicted touchdown point.", null, "https://www.nasa.gov/missions/apollo/apollo-11-mission-overview/");

    public static Reference Suslu2025 { get; } = new("suslu2025", "Süslü, A., Kuran, B. R., Söken, H. E. (2025). Design of a visual pose estimation algorithm for Moon landing. arXiv:2502.14942.", null, "https://arxiv.org/abs/2502.14942", "Crater-based navigation holds the position within 30 m early in the descent and within 2 m later, but degrades in the last 150 s, so the final miss depends on inertial drift. Basis for the 100 m, 1 km and 5 km miss radii.", "Навігація за кратерами тримає положення в межах 30 м на початку спуску і 2 м пізніше, але погіршується в останні 150 с, тож остаточний промах залежить від дрейфу інерціальної навігації. Обґрунтування радіусів промаху 100 м, 1 км і 5 км.");

    public static Reference Gamsky2023 { get; } = new("gamsky2023", "Gamsky, J. N., Metzger, P. T. (2023). The physical state of lunar soil in the permanently shadowed craters of the Moon. arXiv:2305.20007.", null, "https://arxiv.org/abs/2305.20007", "Thermal cycling compacts the soil, so soil in permanently shadowed craters is likely less compacted, weaker and more prone to plume erosion. Not in the touchdown model; stated as a limit.", "Теплові цикли ущільнюють ґрунт, тому ґрунт у вічно затінених кратерах, імовірно, пухкіший, слабший і сильніше розмивається струменем двигуна. У модель посадки не входить; наведено як обмеження.");

    public static Reference Bauer2026 { get; } = new("bauer2026", "Bauer, P., Schwinning, M., Siegel, M. (2026). Adapting a foundation model for lunar surface height estimation. arXiv:2609.02448.", null, "https://arxiv.org/abs/2609.02448", "Stereo terrain models at metre scale cover about 5% of the lunar surface (after Wagner et al.), so lander-scale hazards are mostly unresolved in global data.", "Стереомоделі рельєфу метрового масштабу покривають близько 5% поверхні Місяця (за Wagner et al.), тож небезпеки розміром з апарат у глобальних даних здебільшого не видно.");

    public static Reference CsaMoon { get; } = new("csa-moon", "Canadian Space Agency. Moon exploration: Canadarm3, Gateway, Artemis missions, the Canadian lunar utility rover and the Lunar Exploration Accelerator Program (LEAP).", null, "https://www.asc-csa.gc.ca/eng/astronomy/moon-exploration/");

    public static Reference CsaRover { get; } = new("csa-rover", "Canadian Space Agency. Canadian lunar utility rover on the Moon: concept development, launch planned in the 2030s, designed to survive the lunar night of about two weeks at temperatures down to -200 °C.", null, "https://www.asc-csa.gc.ca/eng/astronomy/moon-exploration/canadian-utility-rover-on-the-moon.asp");

    public static Reference Ispace2021 { get; } = new("ispace2021", "ispace (2021). ispace selected by three Canadian companies (Mission Control Space Services, Canadensys Aerospace, NGC Aerospace), the first awards of the CSA LEAP Capability Demonstration opportunity, for payload delivery and lunar data on Mission 1, 27 May 2021.", null, "https://ispace-inc.com/news-en/?p=2587");

    public static Reference Space2026 { get; } = new("space2026", "Space.com (2026). Canada cancels its 1st moon rover, 25 March 2026. The rover was to land on the rim of Haworth crater on a Firefly CLPS mission planned for 2029.", null, "https://www.space.com/space-exploration/artemis/canada-cancels-its-1st-moon-rover-its-hopefully-not-a-lost-cause/amp/");

    public static Reference Bandfield2011 { get; } = new("bandfield2011", "Bandfield, J. L., Ghent, R. R., Vasavada, A. R., Paige, D. A., Lawrence, S. J., Robinson, M. S. (2011). Lunar surface rock abundance and regolith fines temperatures derived from LRO Diviner Radiometer data. Journal of Geophysical Research 116, E00H02.", "10.1029/2011JE003866", null);

    public static Reference Henriksen2017 { get; } = new("henriksen2017", "Henriksen, M. R. et al. (2017). Extracting accurate and precise topography from LROC narrow angle camera stereo observations. Icarus 283, 122-137.", "10.1016/j.icarus.2016.05.012", null);

    public static Reference Santorelli2026 { get; } = new("santorelli2026", "Santorelli, C. J., Wyglinski, A. M. (2026). LunarRF: a terrain-aware propagation engine and pole-wide 3GPP surface coverage atlas of the lunar south pole. Preprint arXiv:2610.34554v2. Data: LunarRF atlas v2.0, Zenodo, CC BY 4.0, doi:10.5281/zenodo.23301640.", null, "https://arxiv.org/abs/2610.34554", "4 m mast, 1.5 m receiver; at 1.8 GHz a landing-site mast covers a median 94% of its line-of-sight area within 22 km.", "Щогла 4 м, приймач 1.5 м; на 1.8 ГГц щогла біля місця посадки покриває в медіані 94% площі прямої видимості в межах 22 км.");

    public static Reference LunarRfData { get; } = new("lunarrf2026", "Santorelli, C. J., Wyglinski, A. M. (2026). LunarRF atlas v2.0: pole-wide 3GPP surface coverage of the lunar south pole. Zenodo, CC BY 4.0. Candidate table candidates_coverage.csv used for comparison.", "10.5281/zenodo.23301640", null);

    public static Reference Nokia2025 { get; } = new("nokia2025", "Nokia (2025). Nokia and Intuitive Machines deliver first cellular network to the Moon; achieve some key mission objectives. Press release, 10 March 2025.", null, "https://www.nokia.com/newsroom/nokia-and-intuitive-machines-deliver-first-cellular-network-to-the-moon-achieve-some-key-mission-objectives/");

    public static IReadOnlyList<Reference> All { get; } =
    [
        De440, Spice, Iau2015, Nominal, Lola2010, Lola2017, Barker2021, Barker2023, Mazarico2011, Glaser2014, Alsj, Wueller2026, Kopp2011, Au2012, Si2019, Vasavada2012, Williams2017, Dsn810, Oig2023, Fcc2021, Astronomy2025, Afp2024, FireflyM1, Lieske1977, Aoki1982, Wgs84, Horizons, Jones1967, Viper2022, Ochmo2023, Null2023, Fincannon2020, Hayne2017, Paige2010, Pedregosa2011, Rogers1972, Rogers1973, Ali2026, Blanco2024, Rosenburg2011, BlueGhostSize, Jaxa2024, Firefly2025, NasaApollo12, Im2025, Spo2024, NasaApollo11, Suslu2025, Gamsky2023, Bauer2026, CsaMoon, CsaRover, Ispace2021, Space2026, Bandfield2011, Henriksen2017, Santorelli2026, LunarRfData, Nokia2025,
    ];

    public static Reference? Find(string id) => All.FirstOrDefault(item => item.Id == id);

    public static string? Link(Reference reference) => reference.Doi is { } doi ? "https://doi.org/" + doi : reference.Url;
}
