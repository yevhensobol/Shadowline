# Shadowline

Web tool for comparing lunar landing sites and dates: Sun and Earth position against the local horizon, illumination and Earth-visibility windows, and plan robustness: what happens when the launch slips, the lander misses the point or tips over.

Sections: Explore (`/`), Plan (`/plan/where`, `/plan/when`, `/plan/sky`, `/plan/tipover`, report), How it works (`/science/method`, `/science/validation`, `/science/landings`, `/science/about`), Sources (`/sources`).

NASA Space Apps Challenge 2026, challenge "Lunar Landing Site Navigator".

## Highlights

- **The real horizon, not a smooth sphere.** From every site, rays are traced every 0.5° of azimuth up to 300 km over LOLA terrain, including lunar curvature. Sun and Earth come from JPL DE440 through NAIF SPICE, with libration, light time and stellar aberration. Positions are checked against JPL Horizons and temperatures against LRO Diviner (`/science/validation`).
- **What if the plan fails.** Shadowline does not stop at "this site is lit". It shows what happens when the launch slips (the replan assistant finds the nearest dates and sites that still meet the lander's needs), when the lander misses the target point (slopes and lighting within the miss radius), and when it tips over: a touchdown stability model, plus the chance that a tipped lander's panels power it before the battery runs out, for four panel layouts. The models are compared with the real cases of SLIM, IM-1 and IM-2, with slope checks from LROC NAC terrain models.
- **Any point at the south pole.** Maps of yearly sunlight, Earth visibility and slopes at 1 km cover the whole pole. A click anywhere gives the same data as for a catalogued site. Near the pole the 3D view shows 5 m LOLA relief (87-90° S) and the LROC NAC photo mosaic (85.5-90° S), loaded in more detail as the camera gets closer.
- **Everything in the browser.** The site is static. All calculations for user-selected sites and dates run in the browser (Blazor WebAssembly) from precomputed ephemeris and terrain data; there is no server.
- **Open about every number.** Each value has its method and limits on `/science/method`. Every dataset, paper and library is cited with its license on `/sources`.

## Features

- **Explore.** A 3D Moon with the Sun, Earth and stars at the simulation time, and a timeline of sunrise, sunset, Earthrise, Earthset and eclipses. South pole map layers: sunlight, Earth link, slopes, ice and temperature (Diviner), similarity to the Artemis regions (ML) and a 1 km grid. A Zone button outlines 87° S and 84° S.
- **Finding a site.** The landing site finder and recommended sites score points for the mission's needs (power, night length, Earth link) with adjustable weights. Two sites can be compared side by side.
- **Site report.** Sun and Earth over the local horizon, time to sunset and Earthset, panel power and battery for the longest night, direct-to-Earth windows through the Deep Space Network stations, the surface radio link, surface temperature, plan robustness, and an orbital NAC image of the site. The report can be printed or saved as PDF.
- **Surface view.** Stand at the site at a chosen eye height and look at the real horizon with the Sun and Earth. Save a frame or record a short clip.
- **Rover route.** Draw a route on the polar map (80-90° S) and get its length, climb, steepest grade, elevation profile, sunlight and Earth link along the way.
- **History.** 30 landers and impact sites on the map, each with a short note and links. Landing-day replays show the Sun, Earth and shadows at the moment of each landing.
- **Learning.** A guided tour of the tool, and a south pole tour for Artemis: permanent shadow, water ice, lit ridges, Earth visibility, slopes, candidate landing regions and the current Artemis plan, with links to NASA, Wikipedia and papers.
- **Two languages.** English and Ukrainian.

## Stack

.NET 10, Blazor WebAssembly, static hosting. Offline data preparation in Python.

## Build

```
dotnet build
dotnet test
dotnet run --project src/Shadowline.Web
```

## Publish

```
publish.cmd
```

Runs the tests, publishes the site to `deploy/publish/wwwroot` (old output is removed first) and prints the upload command. Options: `tools/publish.ps1 -SkipTests`, `-Output <dir>`.

## Models and data

Data files in `src/Shadowline.Web/wwwroot/data` are built offline by the scripts in `tools/`. Time grid, SPICE kernels, ephemeris, validation, horizons and terrain are run in order by `tools/pipeline.py`. Both ML models are retrained with one command, see `tools/ml/README.md`:

```
pip install -r tools/ml/requirements.txt
python tools/ml/train_all.py --dem LDEM_80S_80MPP_ADJ.TIF --prp dlre_prp_south.tab
```

Method, metrics and errors of every number on the site: `/science/method`. Checks against JPL Horizons and LRO Diviner: `/science/validation`. Data, software and research used: `/sources`.

## Layout

```
src/Shadowline.Core      domain types and computation, no IO
src/Shadowline.Data      readers and validators for data files
src/Shadowline.Web       Blazor WebAssembly UI
src/Shadowline.Tests     xUnit tests
tools/                   Python and C# data and training tools
```

## Team

Rays of Kindness, NASA Space Apps Challenge 2026:

- Maksym Kopystko: presentation, pitching, research
- Yevhen Sobol: development
- Mykyta Ihnatkov: development
- Anna Lychak: design, video
- Mykola Dobosh: research
- Illia Harbaruk: research, testing

## Sources

Every source used by Shadowline. The site shows the same data, software and research with what each is used for at `/sources` (from `src/Shadowline.Web/wwwroot/data/credits.json`), and cites the references below in the method notes and site reports (`src/Shadowline.Core/Reports/References.cs`). Third-party data and software keep their own licenses.

### Data

- **NAIF SPICE kernels and DE440**. NAIF/JPL. Park, R. S. et al., The Astronomical Journal 161, 105 (2021). License: NASA open data. <https://naif.jpl.nasa.gov>
- **JPL Horizons**. NASA/JPL. License: NASA open data. <https://ssd.jpl.nasa.gov/horizons/>
- **LOLA average visibility maps, south pole (avgvisib_65s_240m_201608, avgvisib_85s_060m_201608 and _earth)**. LRO LOLA Team, NASA GSFC. Mazarico, E. et al., Icarus 211, 1066-1081 (2011), doi:10.1016/j.icarus.2010.10.030. NASA PDS Geosciences Node. License: NASA PDS open data. <https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/extras/illumination/img/>
- **LOLA south pole DEM LDEM_80S_80MPP_ADJ**. Barker, M. K. et al., NASA GSFC PGDA, doi:10.60903/gsfcpgda-lola-spole. License: NASA open data. <https://pgda.gsfc.nasa.gov>
- **South Pole LOLA DEM Mosaic, 5 m/px, 87-90 S (ldem_87s_5mpp)**. Barker, M. K. et al. (2021), Improved LOLA Elevation Maps for South Pole Landing Sites: Error Estimates and Their Impact on Illumination Conditions, Planetary and Space Science 203, 105119, doi:10.1016/j.pss.2020.105119. NASA GSFC PGDA. License: NASA open data. <https://pgda.gsfc.nasa.gov/products/81>
- **LRO Diviner Polar Resource Product, south (LRO-L-DLRE-5-PRP-V2.0)**. Paige, D. A. et al., Science 330, 479-482 (2010), doi:10.1126/science.1187726. NASA PDS Geosciences Node. License: NASA PDS open data. <https://pds-geosciences.wustl.edu/lro/urn-nasa-pds-lro_diviner_derived1/data_derived_prp/>
- **CGI Moon Kit, LROC color map**. NASA Scientific Visualization Studio, Ernie Wright, Noah Petro. Data NASA/GSFC/Arizona State University. License: NASA open data. <https://svs.gsfc.nasa.gov/4720>
- **LRO WAC Mosaic, S Pole v2 (LRO_WAC_Mosaic_SPole60_100m_v02)**. The LROC Team, Arizona State University. Served by NASA Moon Trek, NASA/JPL. Robinson, M. S. et al., Space Science Reviews 150, 81-124 (2010), doi:10.1007/s11214-010-9634-2. License: NASA open data. <https://trek.nasa.gov/moon/TrekWS/rest/cat/metadata/fgdc/html?label=LRO_WAC_Mosaic_SPole60_100m_v02>
- **LRO LROC NAC Image Mosaic, S Pole, Avg Merge, Edition 1 (LRO_NAC_AvgMosaic_SPole855_1mp)**. USGS and the LMMP Team (2011), from LROC NAC images by the LROC Team, Arizona State University. Served by NASA Moon Trek, NASA/JPL. License: NASA open data. <https://trek.nasa.gov/moon/TrekWS/rest/cat/metadata/fgdc/html?label=LRO_NAC_AvgMosaic_SPole855_1mp>
- **LRO WAC Mosaic v2 (LRO_WAC_Mosaic_Global_303ppd_v02)**. The LROC Team, Arizona State University. Served by NASA Moon Trek, NASA/JPL. Robinson, M. S. et al., Space Science Reviews 150, 81-124 (2010), doi:10.1007/s11214-010-9634-2. License: NASA open data. <https://trek.nasa.gov/moon/TrekWS/rest/cat/metadata/fgdc/html?label=LRO_WAC_Mosaic_Global_303ppd_v02>
- **Gazetteer of Planetary Nomenclature, Moon (MOON_nomenclature_center_pts)**. IAU Working Group for Planetary System Nomenclature (WGPSN), USGS Astrogeology Science Center. License: IAU/USGS public data. <https://planetarynames.wr.usgs.gov>
- **CGI Moon Kit, LOLA displacement map ldem_16**. NASA Scientific Visualization Studio, LOLA data NASA/GSFC. License: NASA open data. <https://svs.gsfc.nasa.gov/4720>
- **LOLA Gridded Data Record LDEM_64**. LOLA Science Team, NASA/GSFC, PDS Geosciences Node. License: NASA open data. <https://pds-geosciences.wustl.edu/lro/lro-l-lola-3-rdr-v1/lrolol_1xxx/data/lola_gdr/cylindrical/img/>
- **Blue Marble Next Generation, November**. NASA Earth Observatory, Reto Stöckli, NASA GSFC. License: NASA open data. <https://science.nasa.gov/earth/earth-observatory/blue-marble-next-generation/base-topography-bathymetry>
- **Earth at Night, Black Marble 2016**. NASA Earth Observatory, Joshua Stevens, Suomi NPP VIIRS data, Miguel Román, NASA GSFC. License: NASA open data. <https://science.nasa.gov/earth/earth-observatory/earth-at-night/maps>
- **The Blue Marble, clouds**. NASA Goddard Space Flight Center, MODIS. License: NASA open data. <https://science.nasa.gov/earth/earth-observatory/the-blue-marble-true-color-global-imagery-at-1km-resolution/>
- **Deep Star Maps 2020**. NASA/Goddard Space Flight Center Scientific Visualization Studio. Gaia DR2: ESA/Gaia/DPAC. License: NASA open data, Gaia DR2 credit. <https://svs.gsfc.nasa.gov/4851>
- **Apollo landing sites**. NASA NSSDCA, Wagner, R. V. et al., Icarus 283, 92-103 (2017). License: NASA open data. <https://nssdc.gsfc.nasa.gov/planetary/lunar/lunar_sites.html>
- **LROC coordinates of human features (2016) and LROC site images**. NASA/GSFC/Arizona State University. License: LROC terms of use: website images are copyrighted, educational use with credit. <https://lroc.im-ldi.com/about/terms>
- **LROC NAC digital terrain models NAC_DTM_MALAPERTA01 and NAC_DTM_NOBILE03**. NASA/GSFC/Arizona State University. Henriksen, M. R. et al., Icarus 283, 122-137 (2017), doi:10.1016/j.icarus.2016.05.012. License: NASA PDS open data. <https://data.lroc.im-ldi.com/lroc/view_rdr/NAC_DTM_MALAPERTA01>
- **LRO Diviner global high-resolution mosaic, rock abundance at slope-adjusted midnight (dghrm_ra_sam_70s70n)**. Diviner Lunar Radiometer team. Bandfield, J. L. et al., JGR 116, E00H02 (2011), doi:10.1029/2011JE003866. NASA PDS Geosciences Node, doi:10.17189/wj0s-w188. License: NASA PDS open data. <https://pds-geosciences.wustl.edu/lro/urn-nasa-pds-lro_diviner_derived1/data_derived_ghrm/>
- **LunarRF atlas v2.0: pole-wide 3GPP surface coverage of the lunar south pole**. Santorelli, C. J., Wyglinski, A. M. (2026), Worcester Polytechnic Institute. Preprint arXiv:2610.34554v2, data doi:10.5281/zenodo.23301640. License: CC BY 4.0. <https://doi.org/10.5281/zenodo.23301640>
- **LRO images Odysseus lander**. NASA/GSFC/Arizona State University. License: NASA open data. <https://www.nasa.gov/missions/lro/nasas-lro-images-intuitive-machines-odysseus-lander/>
- **Complementary Data for Wueller et al. (2026): Assessing Potential Landing Sites with Favorable Illumination and Accessible, Potentially Volatile-Rich PSRs within Artemis Candidate Landing Regions**. Wueller, L. et al. Data: Zenodo, doi:10.5281/zenodo.17084058. Paper: JGR Planets 131, e2025JE009434 (2026), doi:10.1029/2025JE009434. License: CC BY 4.0. <https://doi.org/10.5281/zenodo.17084058>
- **Landing times**. NASA NSSDCA mission pages; NASA and LROC news for IM-1, SLIM, Chang'e 4 to 6 and Blue Ghost M1; press reports for Chandrayaan-3 and IM-2. License: NASA open data, facts from public reports. <https://nssdc.gsfc.nasa.gov/nmc/spacecraft/display.action?id=1969-059C>
- **SLIM landing position**. NASA/GSFC/Arizona State University. License: NASA open data. <https://www.nasa.gov/missions/lro/nasas-lro-spots-japans-moon-lander>
- **Planned landing targets (approximate)**. Public mission reports: Chang'e 7 near 88.8 S, 123.4 E (Shackleton rim); Blue Moon MK1 at the Shackleton connecting ridge; IM-3 at Reiner Gamma (7.5 N, 59.0 W). License: Facts from public reports. <https://www.nasa.gov/event/clps-flight-blue-origins-blue-moon-mark-1/>
- **Mission reports of SLIM, IM-1, IM-2 and Blue Ghost M1**. JAXA, Intuitive Machines, Firefly Aerospace, NASA, as quoted by the sources listed in incidents.json. License: Facts cited with links. <https://global.jaxa.jp/press/2024/01/20240125-1_e.html>
- **JAXA ground station coordinates (ITRF2014)**. JAXA ISAS. License: Facts cited with link. <https://ddor.nav.isas.jaxa.jp/station_coordinates>

### Research and references

- Pike, R. J., in Impact and Explosion Cratering, University of Arizona Press (1977), 489-509. *Size-dependence in the shape of fresh impact craters on the Moon*. <https://ui.adsabs.harvard.edu/abs/1977iecp.symp..489P/abstract>
- Stopar, J. D. et al., Icarus 298, 34-48 (2017), doi:10.1016/j.icarus.2017.05.022. *Relative depths of simple craters and the nature of the lunar regolith*. <https://doi.org/10.1016/j.icarus.2017.05.022>
- Gläser, P. et al., EPSC Abstracts 9, EPSC2014-136 (2014). *Connecting Ridge: a landing site at the lunar south pole with extended illumination*. <https://elib.dlr.de/94242/1/EPSC2014-136-1.pdf>
- Park, R. S., Folkner, W. M., Williams, J. G., Boggs, D. H. (2021). The JPL Planetary and Lunar Ephemerides DE440 and DE441. The Astronomical Journal 161, 105. doi:[10.3847/1538-3881/abd414](https://doi.org/10.3847/1538-3881/abd414)
- Acton, C. H. (1996). Ancillary data services of NASA's Navigation and Ancillary Information Facility. Planetary and Space Science 44, 65-70. Kernels used: de440s.bsp, moon_pa_de440_200625.bpc, moon_de440_250416.tf, naif0012.tls. doi:[10.1016/0032-0633(95)00107-7](https://doi.org/10.1016/0032-0633(95)00107-7) <https://naif.jpl.nasa.gov/pub/naif/generic_kernels/>
- Archinal, B. A. et al. (2018). Report of the IAU Working Group on Cartographic Coordinates and Rotational Elements: 2015. Celestial Mechanics and Dynamical Astronomy 130, 22. doi:[10.1007/s10569-017-9805-5](https://doi.org/10.1007/s10569-017-9805-5)
- Prša, A. et al. (2016). Nominal values for selected solar and planetary quantities: IAU 2015 Resolution B3. The Astronomical Journal 152, 41. doi:[10.3847/0004-6256/152/2/41](https://doi.org/10.3847/0004-6256/152/2/41)
- Smith, D. E. et al. (2010). Initial observations from the Lunar Orbiter Laser Altimeter (LOLA). Geophysical Research Letters 37, L18204. doi:[10.1029/2010GL043751](https://doi.org/10.1029/2010GL043751)
- Smith, D. E. et al. (2017). Summary of the results from the Lunar Orbiter Laser Altimeter after seven years in lunar orbit. Icarus 283, 70-91. Global DEM ldem_16 (16 pixels per degree), NASA PDS. doi:[10.1016/j.icarus.2016.06.006](https://doi.org/10.1016/j.icarus.2016.06.006) <https://pds-geosciences.wustl.edu/missions/lro/lola.htm>
- Barker, M. K. et al. (2021). Improved LOLA elevation maps for south pole landing sites: Error estimates and their impact on illumination conditions. Planetary and Space Science 203, 105119. doi:[10.1016/j.pss.2020.105119](https://doi.org/10.1016/j.pss.2020.105119)
- Barker, M. K. et al. (2023). A new view of the lunar south pole from the Lunar Orbiter Laser Altimeter (LOLA). The Planetary Science Journal 4, 183. Product LDEM_80S_80MPP_ADJ, data doi:10.60903/gsfcpgda-lola-spole. doi:[10.3847/PSJ/acf3e1](https://doi.org/10.3847/PSJ/acf3e1) <https://pgda.gsfc.nasa.gov/products/90>
- Mazarico, E., Neumann, G. A., Smith, D. E., Zuber, M. T., Torrence, M. H. (2011). Illumination conditions of the lunar polar regions using LOLA topography. Icarus 211, 1066-1081. doi:[10.1016/j.icarus.2010.10.030](https://doi.org/10.1016/j.icarus.2010.10.030)
- Scotti, J. V. Apollo Lunar Surface Journal: Earth and Sun altitude and azimuth at the Apollo landing sites. NASA History. <https://www.nasa.gov/history/alsj/alsj-EarthSun_AltAz.html>
- Wueller, L. et al. (2026). Assessing potential landing sites with favorable illumination and accessible, potentially volatile-rich PSRs within Artemis candidate landing regions. Journal of Geophysical Research: Planets 131, e2025JE009434. Data: Zenodo record 17084058, CC BY 4.0. Work from the 2024 LPI Exploration Science Summer Intern Program, not a NASA site selection. doi:[10.1029/2025JE009434](https://doi.org/10.1029/2025JE009434) <https://doi.org/10.5281/zenodo.17084058>
- Kopp, G., Lean, J. L. (2011). A new, lower value of total solar irradiance: Evidence and climate significance. Geophysical Research Letters 38, L01706. doi:[10.1029/2010GL045777](https://doi.org/10.1029/2010GL045777)
- IAU (2012). Resolution B2 on the re-definition of the astronomical unit of length: 1 au = 149 597 870 700 m. <https://iauarchive.eso.org/static/resolutions/IAU2012_English.pdf>
- BIPM (2019). The International System of Units (SI), 9th edition: c = 299 792 458 m/s. <https://www.bipm.org/en/publications/si-brochure>
- Vasavada, A. R. et al. (2012). Lunar equatorial surface temperatures and regolith properties from the Diviner Lunar Radiometer Experiment. Journal of Geophysical Research: Planets 117. doi:[10.1029/2011JE003987](https://doi.org/10.1029/2011JE003987)
- Williams, J.-P., Paige, D. A., Greenhagen, B. T., Sefton-Nash, E. (2017). The global surface temperatures of the Moon as measured by the Diviner Lunar Radiometer Experiment. Icarus 283, 300-325. doi:[10.1016/j.icarus.2016.08.012](https://doi.org/10.1016/j.icarus.2016.08.012)
- Intuitive Machines (2021). NOVA-C Lunar Lander and IM-1 Mission, Technical Appendix (Attachment A), FCC application SAT-LOA-20210423-00055: three fixed body-mounted solar panels, 788 W maximum, Li-ion batteries 1554 Wh, unregulated 28 V bus, lunar surface phase 14 days. <https://fcc.report/IBFS/SAT-LOA-20210423-00055/6378380.pdf>
- Astronomy (2025). Intuitive Machines preps IM-2 mission for Moon launch: three solar panels give 200 W on the surface for one lunar day; the battery does not carry Nova-C through the lunar night. <https://www.astronomy.com/space-exploration/intuitive-machines-preps-im-2-mission-for-moon-launch-this-week/>
- AFP (2024). US Moon lander's battery likely has hours left: company, 28 February 2024. Quotes the Intuitive Machines IM-1 update: battery life may continue up to an additional 10-20 hours. <https://phys.org/news/2024-02-moon-lander-battery-hours-left.html>
- Firefly Aerospace. Blue Ghost Mission 1: two side solar panels and one on the top deck provide up to 400 W. <https://fireflyspace.com/missions/blue-ghost-mission-1/>
- NASA Office of Inspector General (2023). Audit of NASA's Deep Space Network, IG-23-016: the antennas operate at capacity and are oversubscribed. <https://oig.nasa.gov/docs/IG-23-016.pdf>
- JPL (2024). DSN Telecommunications Link Design Handbook 810-005, module 301: Coverage and Geometry. <https://deepspace.jpl.nasa.gov/dsndocs/810-005/>
- Lieske, J. H., Lederle, T., Fricke, W., Morando, B. (1977). Expressions for the precession quantities based upon the IAU (1976) system of astronomical constants. Astronomy and Astrophysics 58, 1-16. <https://ui.adsabs.harvard.edu/abs/1977A%26A....58....1L>
- Aoki, S. et al. (1982). The new definition of Universal Time. Astronomy and Astrophysics 105, 359-361. <https://ui.adsabs.harvard.edu/abs/1982A%26A...105..359A>
- NIMA (2000). Department of Defense World Geodetic System 1984, Technical Report TR8350.2, 3rd edition.
- Giorgini, J. D. et al. (1996). JPL's on-line Solar System data service. Bulletin of the American Astronomical Society 28, 1158. <https://ssd.jpl.nasa.gov/horizons/>
- Jones, R. L. (1967). An investigation of Earthshine lighting conditions for lunar-surface operations. NASA Manned Spacecraft Center. <https://ntrs.nasa.gov/citations/19680001705>
- Shirley, M., Balaban, E. (2022). Overview of mission planning for the VIPER rover. NASA Ames Research Center. <https://www.nasa.gov/wp-content/uploads/2022/05/overview_of_mission_planning_for_the_viper_rover.pdf>
- NASA Office of the Chief Health and Medical Officer (2022). Lighting considerations for Artemis, technical brief OCHMO-TB-001 Rev A. <https://www.nasa.gov/wp-content/uploads/2023/03/artemis-lighting-considerations-overview-technical-brief-ochmo.pdf>
- Null, C. et al. (2023). Identification of risks to EVA derived from lighting conditions at the lunar south pole. IAASS 2023. <https://ntrs.nasa.gov/citations/20230006101>
- Fincannon, H. J. (2020). Solar power generation. NASA Glenn Research Center. <https://ntrs.nasa.gov/citations/20205002844>
- Hayne, P. O. et al. (2017). Global regolith thermophysical properties of the Moon from the Diviner Lunar Radiometer Experiment. Journal of Geophysical Research: Planets 122, 2371-2400. doi:[10.1002/2017JE005387](https://doi.org/10.1002/2017JE005387)
- Pedregosa, F. et al. (2011). Scikit-learn: Machine Learning in Python. Journal of Machine Learning Research 12, 2825-2830. <https://jmlr.org/papers/v12/pedregosa11a.html>
- Paige, D. A. et al. (2010). Diviner Lunar Radiometer observations of cold traps in the Moon's south polar region. Science 330, 479-482. Polar Resource Product LRO-L-DLRE-5-PRP. doi:[10.1126/science.1187726](https://doi.org/10.1126/science.1187726)
- Rogers, W. F. (1972). Apollo experience report: Lunar module landing gear subsystem. NASA TN D-6850, Manned Spacecraft Center. <https://ntrs.nasa.gov/citations/19720018253>
- Rogers, W. F. (1973). Apollo lunar module landing gear. NASA Technical Reports Server 19730010151, pp. 123-134. <https://ntrs.nasa.gov/citations/19730010151>
- Ali, R. M., Slagle, A. C., Mordasiewicz, A., Orphee, J., McDonough, P. J. (2026). Apollo 11 lunar module touchdown dynamics reconstruction verification and validation for human landing systems. NASA Technical Reports Server 20260001760. <https://ntrs.nasa.gov/citations/20260001760>
- Blanco, P., Mungan, C. E. (2024). Tripping on the Moon. Physics Education 59, 043011. doi:[10.1088/1361-6552/ad539c](https://doi.org/10.1088/1361-6552/ad539c)
- Rosenburg, M. A. et al. (2011). Global surface slopes and roughness of the Moon from the Lunar Orbiter Laser Altimeter. Journal of Geophysical Research 116, E02001. doi:[10.1029/2010JE003716](https://doi.org/10.1029/2010JE003716)
- AmericaSpace (2025). Blue Ghost lander progresses through checkouts. Dimensions from Firefly Aerospace: 3.5 m wide, 2 m tall, four legs spanning 3 m. <https://www.americaspace.com/2025/01/30/blue-ghost-lander-progresses-through-checkouts/>
- JAXA (2024). SLIM press conference, 25 January 2024, as reported by SpacePolicyOnline: SLIM landed 55 m east of the target. <https://spacepolicyonline.com/news/japans-slim-lunar-lander-notches-more-successes/>
- Firefly Aerospace (2025). Firefly Aerospace becomes first commercial company to successfully land on the Moon, 2 March 2025: touchdown within the 100 m landing target. <https://investors.fireflyspace.com/node/6786/pdf>
- NASA (2024). 55 years ago: Apollo 12 makes a pinpoint landing on the Moon. Intrepid landed 538 ft (164 m) from Surveyor 3. <https://www.nasa.gov/history/55-years-ago-apollo-12-makes-a-pinpoint-landing-on-the-moon>
- Intuitive Machines (2025). Intuitive Machines executes southernmost lunar landing and operates payloads, 7 March 2025: Athena landed 250 m from the intended site. NASA reported more than 400 m. <https://investors.intuitivemachines.com/news-releases/news-release-details/intuitive-machines-executes-southernmost-lunar-landing-and>
- SpacePolicyOnline (2024). NASA and IM agree Odysseus is a success: LRO located the lander about 1.5 km from its intended site on a 12° slope. <https://spacepolicyonline.com/news/nasa-and-im-agree-odysseus-is-a-success/>
- NASA. Apollo 11 mission overview: Eagle landed about four miles downrange from the predicted touchdown point. <https://www.nasa.gov/missions/apollo/apollo-11-mission-overview/>
- Süslü, A., Kuran, B. R., Söken, H. E. (2025). Design of a visual pose estimation algorithm for Moon landing. arXiv:2502.14942. <https://arxiv.org/abs/2502.14942>
- Gamsky, J. N., Metzger, P. T. (2023). The physical state of lunar soil in the permanently shadowed craters of the Moon. arXiv:2305.20007. <https://arxiv.org/abs/2305.20007>
- Bauer, P., Schwinning, M., Siegel, M. (2026). Adapting a foundation model for lunar surface height estimation. arXiv:2609.02448. <https://arxiv.org/abs/2609.02448>
- Canadian Space Agency. Moon exploration: Canadarm3, Gateway, Artemis missions, the Canadian lunar utility rover and the Lunar Exploration Accelerator Program (LEAP). <https://www.asc-csa.gc.ca/eng/astronomy/moon-exploration/>
- Canadian Space Agency. Canadian lunar utility rover on the Moon: concept development, launch planned in the 2030s, designed to survive the lunar night of about two weeks at temperatures down to -200 °C. <https://www.asc-csa.gc.ca/eng/astronomy/moon-exploration/canadian-utility-rover-on-the-moon.asp>
- ispace (2021). ispace selected by three Canadian companies (Mission Control Space Services, Canadensys Aerospace, NGC Aerospace), the first awards of the CSA LEAP Capability Demonstration opportunity, for payload delivery and lunar data on Mission 1, 27 May 2021. <https://ispace-inc.com/news-en/?p=2587>
- Space.com (2026). Canada cancels its 1st moon rover, 25 March 2026. The rover was to land on the rim of Haworth crater on a Firefly CLPS mission planned for 2029. <https://www.space.com/space-exploration/artemis/canada-cancels-its-1st-moon-rover-its-hopefully-not-a-lost-cause/amp/>
- Bandfield, J. L., Ghent, R. R., Vasavada, A. R., Paige, D. A., Lawrence, S. J., Robinson, M. S. (2011). Lunar surface rock abundance and regolith fines temperatures derived from LRO Diviner Radiometer data. Journal of Geophysical Research 116, E00H02. doi:[10.1029/2011JE003866](https://doi.org/10.1029/2011JE003866)
- Henriksen, M. R. et al. (2017). Extracting accurate and precise topography from LROC narrow angle camera stereo observations. Icarus 283, 122-137. doi:[10.1016/j.icarus.2016.05.012](https://doi.org/10.1016/j.icarus.2016.05.012)
- Santorelli, C. J., Wyglinski, A. M. (2026). LunarRF: a terrain-aware propagation engine and pole-wide 3GPP surface coverage atlas of the lunar south pole. Preprint arXiv:2610.34554v2. Data: LunarRF atlas v2.0, Zenodo, CC BY 4.0, doi:10.5281/zenodo.23301640. <https://arxiv.org/abs/2610.34554>
- Santorelli, C. J., Wyglinski, A. M. (2026). LunarRF atlas v2.0: pole-wide 3GPP surface coverage of the lunar south pole. Zenodo, CC BY 4.0. Candidate table candidates_coverage.csv used for comparison. doi:[10.5281/zenodo.23301640](https://doi.org/10.5281/zenodo.23301640)
- Nokia (2025). Nokia and Intuitive Machines deliver first cellular network to the Moon; achieve some key mission objectives. Press release, 10 March 2025. <https://www.nokia.com/newsroom/nokia-and-intuitive-machines-deliver-first-cellular-network-to-the-moon-achieve-some-key-mission-objectives/>

### Software

- **three.js r187**. three.js authors. License: MIT. <https://threejs.org>
- **Geologica**. The Geologica Project Authors, via Fontsource. License: SIL Open Font License 1.1. <https://fontsource.org/fonts/geologica>
- **Oswald**. The Oswald Project Authors, via Fontsource. License: SIL Open Font License 1.1. <https://fontsource.org/fonts/oswald>
- **Roboto Condensed**. Google, via Fontsource. License: SIL Open Font License 1.1. <https://fontsource.org/fonts/roboto-condensed>
- **Spectral**. The Spectral Project Authors, via Fontsource. License: SIL Open Font License 1.1. <https://fontsource.org/fonts/spectral>
- **.NET 10 and ASP.NET Core Blazor WebAssembly**. .NET Foundation and contributors. License: MIT. <https://github.com/dotnet/aspnetcore>
- **xUnit.net 2.9**. .NET Foundation and contributors. License: Apache-2.0. <https://xunit.net>
- **Python 3**. Python Software Foundation. License: PSF License 2.0. <https://www.python.org>
- **NumPy**. Harris, C. R. et al., Nature 585, 357-362 (2020), doi:10.1038/s41586-020-2649-2. License: BSD-3-Clause. <https://numpy.org>
- **SciPy**. Virtanen, P. et al., Nature Methods 17, 261-272 (2020), doi:10.1038/s41592-019-0686-2. License: BSD-3-Clause. <https://scipy.org>
- **scikit-learn**. Pedregosa, F. et al., JMLR 12, 2825-2830 (2011). License: BSD-3-Clause. <https://scikit-learn.org>
- **rasterio**. Gillies, S. et al., Mapbox and contributors. License: BSD-3-Clause. <https://github.com/rasterio/rasterio>
- **GDAL**. GDAL/OGR contributors, Open Source Geospatial Foundation, doi:10.5281/zenodo.5884351. License: MIT. <https://gdal.org>
- **pyproj and PROJ**. Snow, A. D. et al.; PROJ contributors, doi:10.5281/zenodo.5884394. License: MIT. <https://pyproj4.github.io/pyproj>
- **SpiceyPy and the NAIF SPICE Toolkit**. Annex, A. M. et al., JOSS 5(50), 2050 (2020), doi:10.21105/joss.02050; NAIF/JPL. License: MIT; SPICE Toolkit NAIF rules. <https://github.com/AndrewAnnex/SpiceyPy>
- **jplephem**. Rhodes, B.. License: MIT. <https://github.com/brandon-rhodes/python-jplephem>
- **Astropy**. Astropy Collaboration, ApJ 935, 167 (2022), doi:10.3847/1538-4357/ac7c74. License: BSD-3-Clause. <https://www.astropy.org>
- **OpenCV**. Bradski, G., Dr. Dobb's Journal of Software Tools (2000); OpenCV contributors. License: Apache-2.0. <https://opencv.org>
- **Pillow**. Clark, A. and contributors. License: MIT-CMU (HPND). <https://python-pillow.org>
- **OpenEXR**. Academy Software Foundation. License: BSD-3-Clause. <https://openexr.com>
- **pyshp**. Lawhead, J. and contributors. License: MIT. <https://github.com/GeospatialPython/pyshp>
- **tifffile**. Gohlke, C.. License: BSD-3-Clause. <https://github.com/cgohlke/tifffile>
- **LROC QuickMap**. Applied Coherent Technology and the LROC team, Arizona State University; hosted by Intuitive Machines. License: Linked web application. <https://quickmap.lroc.im-ldi.com/>

### Learning content

The south pole tour (`src/Shadowline.Web/wwwroot/data/polar-tour.json`) and the lander notes (`src/Shadowline.Web/wwwroot/data/missions.json`) link to 75 Wikipedia articles in English and Ukrainian and to these pages:

- [NASA NSSDC: landing site coordinates](https://nssdc.gsfc.nasa.gov/planetary/lunar/lunar_sites.html)
- [LROC: coordinates of landers](https://lroc.im-ldi.com/data/support/downloads/2016_LROC_Coordinates_of_Human_Features.pdf)
- [Chang'e 4 (Von Kármán): LRO image of the site](https://lroc.im-ldi.com/images/1087)
- [Chang'e 5 (Mons Rümker): LRO image of the site](https://lroc.im-ldi.com/images/1172)
- [Chang'e 6 (Apollo basin): LRO image of the site](https://lroc.im-ldi.com/images/1374)
- [SLIM (Shioli crater): NASA: LRO image of the lander](https://www.nasa.gov/missions/lro/nasas-lro-spots-japans-moon-lander)
- [Blue Ghost Mission 1 (Mare Crisium): LRO image of the site](https://lroc.im-ldi.com/images/1407)
- [Chandrayaan-3 Vikram: LRO image of the site](https://lroc.im-ldi.com/images/1314)
- [IM-1 Odysseus (Malapert A): NASA: LRO image of the lander](https://www.nasa.gov/missions/lro/nasas-lro-images-intuitive-machines-odysseus-lander/)
- [IM-2 Athena (Mons Mouton): LRO image of the site](https://lroc.im-ldi.com/images/1408)
- [Luna 25 impact site (Pontécoulant G): LRO image of the site](https://lroc.im-ldi.com/images/1311)
- [HAKUTO-R Mission 1 impact site (Atlas crater): LRO image of the site](https://lroc.im-ldi.com/images/1302)
- [RESILIENCE impact site (Mare Frigoris): LRO image of the site](https://lroc.im-ldi.com/images/1456)
- [Beresheet impact site (Mare Serenitatis): LRO image of the site](https://lroc.im-ldi.com/images/1101)
- [NASA PGDA: lunar polar illumination maps](https://pgda.gsfc.nasa.gov/products/69)
- [Colaprete et al. 2010, Science: water in the LCROSS plume](https://doi.org/10.1126/science.1186986)
- [Mazarico et al. 2011, Icarus: illumination of the lunar poles](https://doi.org/10.1016/j.icarus.2010.10.030)
- [NASA: update on the Artemis landing regions (2024)](https://www.nasa.gov/news-release/nasa-provides-update-on-artemis-iii-moon-landing-regions/)
- [NASA NTRS: down-selection from 13 to 9 regions](https://ntrs.nasa.gov/citations/20250011660)
- [NASA: Artemis II crew back on Earth](https://www.nasa.gov/news-release/nasa-welcomes-record-setting-artemis-ii-moonfarers-back-to-earth/)
- [NASA: Artemis IV mission](https://www.nasa.gov/mission/artemis-iv/)
- [The Planetary Society: Chang'e-7](https://www.planetary.org/space-missions/change-7)
- [NASA PGDA: LOLA 5 m south pole DEM](https://pgda.gsfc.nasa.gov/products/81)
- [NASA Moon Trek](https://trek.nasa.gov/moon/)

## Use of AI

Declaration for the NASA Space Apps Challenge 2026 (also on the site at `/sources`). The team chose the approach and the data, checked the results against the cited sources and is responsible for the content.

- Claude (Anthropic): writing and reviewing code (C# and Blazor, JavaScript for the 3D scene, Python for the data pipeline and the ML models), processing and checking data, searching for sources, drafting site texts in English and Ukrainian, documentation; it read the team's interface design in Figma through the Figma MCP connector.
- Elicit: search and screening of scientific publications.
- Gemini and NotebookLM (Google): research and summaries of sources.

No value on the site is produced by a language model. Positions, relief, lighting, temperatures and probabilities are computed by scripts from the cited data; the Artemis similarity layer is a trained model described at `/science/method`. Orbital photos and textures come from NASA and partner archives and are not AI-generated.

## License

Code: MIT, see `LICENSE`. Data and third-party software keep their own licenses, listed at `/sources`.
