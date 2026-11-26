# Gradient Magnetodielectric Medium Research Plan

Date: 2026-10-18. Project: Small Antenna / Gradient Artificial Medium with Embedded Radiator.

## Objective and scope

Produce reproducible samples with controllable complex relative permittivity εr(f) and permeability μr(f), then evaluate the layer-by-layer profile and wave propagation. Mechanical pressure is a separate, final experiment; delay or SWR measurements cannot establish it.

10–12 MHz is not the target frequency range. The preferred initial research window is 0.5–1 MHz, with measurements below and above it as far as the selected method allows. The final frequency will be selected based on sample properties, losses, and measurement resolution. High initial permeability alone is not a selection criterion.

This is a research plan, not an approved purchase or a validated formulation. No supplier inquiries have been sent. Stock, minimum order quantities, prices, shipping, and the availability of technical support from specialist suppliers require confirmation.

## 1. Reviewed supply options

The official pages listed below were reviewed. A catalog listing means that the supplier offers the relevant material class; suitability for this project has not yet been confirmed.

| Supplier | Confirmed offering | Questions to resolve |
|---|---|---|
| Powder Processing & Technology, USA | NiZn(Cu): ready-to-press powders with organic additives; low-temperature LSF series; fully sintered loading powders for polymer resins | Specific grades for 0.1–5 MHz, complex sample μ/ε, availability of small samples, formulations and processing conditions |
| Steward Advanced Materials, USA | The published TDS lists MnZn loading powder 73399 for resins; composition and particle-size variations are possible | Continued availability, small quantities, μ/ε and loss parameters; the material targets EMI absorption, and suitability for a low-loss medium is not established |
| NMD Metalpowders, Germany | NiZn ferrite powder, item 3244, supplied on request; publicly positioned for frequencies above 10 MHz | Lower-frequency data, grades, minimum order quantity, measurement method; the page provides no numerical μ/ε values |
| TODA Materials, South Korea | MnZn powder series, including core materials and sintered, milled powders for composites | Availability of laboratory samples; which tables describe sintered cores versus composites; complex ε |
| Fair-Rite, USA | Sintered powders 68/77, including 100 g samples | Data for powders and recommended composites; previously cited μi=16/2000 values apply to standard solid samples, not a mixture |

Sources:

- https://www.pptechnology.com/soft-ferrite-powders
- https://www.stewardmaterials.com/wp-content/uploads/Steward-Ceramic-Materials-Technical-Data-Sheet-1.pdf
- https://nmd-metalpowders.de/en/nizn-ferritpulver/
- https://www.todamaterials.co.kr/kor/business/getBiz1001Html.do
- https://fair-rite.com/product/sintered-ferrite-powder-0pm68sp100s/
- https://fair-rite.com/product/sintered-ferrite-powder-0pm77sp100s/

First priorities for technical inquiries: PPT and Fair-Rite; Steward is an additional candidate for discussing filler properties. NMD and TODA are backup options. This priority reflects the relevance of their published offerings, not a quality ranking.

## 2. Select the processing route first

### A. Ferrite-polymer composite — recommended initial route

A prepared ferrite filler is mixed with a characterized binder, molded, and cured according to the binder's specified schedule. This is not sintering. The aim is to produce small, homogeneous samples and measure their properties at different volume fractions. Binder properties, porosity, particle settling, and electrode contact are part of the experiment.

PPT explicitly offers fully sintered NiZn(Cu) loading powders for polymer resins. The availability of this process does not guarantee equal μ and ε or low losses; these must be checked in the resulting composite.

### B. Sintered ceramics — a separate, subsequent route

Pressing and sintering require a suitable powder, removal of the temporary binder, a thermal cycle, and, depending on composition, atmosphere control. Do not assume that every loading powder is suitable for resintering.

Ferroxcube's description of typical manufacturing specifies temperatures of 1150–1300 °C, linear shrinkage of up to 20%, and atmosphere control. This illustrates the scale of the process; it is NOT a recipe for our powders. PPT also offers special low-temperature series; their processing conditions should be obtained from the manufacturer.

Source: https://www.ferroxcube.com/en-global/download/download/11

Do not buy a furnace before selecting the composition and obtaining a process specification. It is advisable to order the first ceramic samples from a manufacturer or produce them with a laboratory. The first layers are best manufactured separately and assembled mechanically: co-sintering adds concerns about differential shrinkage, cracking, interlayer diffusion, and preservation of the gradient.

## 3. Stages and progression criteria

| Stage | Work | Result / progression criterion |
|---|---|---|
| 0. Requirements | Define the future test geometry, frequency range, slowdown target, and acceptable size/losses | One-page technical specification; numerical loss and mismatch limits selected for the experiment |
| 1. Supplier data | Compare grades, powder states, and measured sample data | Table of 2–3 candidates with known and missing parameters; unknowns are explicitly listed |
| 2. Measurement validation | Prepare fixtures and measure air, binder, and reference materials | Repeatability, parasitic effects, and resolution assessed; detection limit understood |
| 3. Homogeneous samples | No more than two powders; three reproducible volume fractions each, plus unfilled binder | Geometry, density, composition, and complex properties recorded for every sample |
| 4. Selection | Check μ′, μ″, ε′, ε″ and their frequency dependence | A useful window identified, or an honest finding that the candidates are unsuitable |
| 5. Three layers | Assemble a stepped profile in a defined direction using measured formulations | Differences from a homogeneous control and from calculations accounting for all interfaces evaluated |
| 6. Manufacturing choice | Compare the composite with available ceramic data | Decision to continue with the composite or order sintered samples; a furnace is not required |
| 7. Force model and experiment | Calculate forces on all parts and the expected signal; prepare artifact controls | Separate mechanical measurement; force enhancement distinguished from net thrust of the entire setup |

Each stage has a measurable result. Repeated general board sweeps do not replace these results. Do not retune the existing board or simulator at this stage.

## 4. Information to request from suppliers

The inquiry should describe laboratory research into a medium, rather than an EMI absorber: high real magnetic/electric response, a controllable ratio between them, and low losses are desirable. Request frequency-dependent data around 0.5–1 MHz, preferably 0.1–5 MHz, without treating this range as a rigid requirement.

1. Exact grade, composition/family (MnZn, NiZn, NiZnCu), batch number, and current state: precursor, calcined, fully sintered and milled, or ready-to-press.
2. μ′(f), μ″(f), ε′(f), ε″(f). Specify whether the values describe solid ceramics, compacted powder, or a composite.
3. Measurement method, geometry, temperature, excitation level, magnetic bias, density, porosity, and uncertainty.
4. For composites: binder, its properties, ferrite volume fraction, and curing schedule. Mass and volume fractions are not interchangeable.
5. Particle-size distribution d10/d50/d90, true and bulk/compacted densities, coating, moisture, SDS, and certificate of analysis.
6. For the ceramic route: suitability for pressing, recommended pressure/binder, organic removal, temperature/dwell time/atmosphere/cooling, shrinkage, and whether different grades can be co-fired.
7. Sample size, minimum order quantity, cost, lead time, and availability of prepared reference samples or measurement services.

Missing ε data do not prove unsuitability, but they rule out claiming that the material is impedance-matched. If the supplier has no data, a sample may be purchased explicitly for exploratory research after a measurement procedure has been prepared.

## 5. Low-frequency measurement strategy

At 0.5–1 MHz, it is advisable to separate magnetic and electric measurements:

- μ: a toroidal sample and inductive fixture; account for conductor resistance, parasitic capacitance, field level, and geometry.
- ε: a flat sample between electrodes with known area/thickness; account for gaps, fringing fields, conductivity, contact, and guard electrodes where needed.
- Instrument: an LCR/impedance analyzer covering the selected frequencies and required impedance/loss ranges, or an external laboratory. A low-frequency LCR meter with fixed 1–100 kHz settings is not automatically suitable for 1 MHz.
- NanoVNA: potentially useful for preliminary impedance checks and subsequent line testing, but its adequacy for extracting all four properties of a small sample must be verified. Good SWR alone is insufficient.

Resolution example, not a prediction for the powder: for a 20 mm sample length and nondispersive n=10, the additional phase relative to air at 1 MHz is only about 0.216°. Direct parameter extraction from transmission through a short sample may therefore be ill-conditioned. Before purchasing an instrument, establish that calibration and repeatability can resolve the expected effect.

The parallel-plate and inductive-fixture methods are supported by Keysight documentation:
https://helpfiles.keysight.com/csg/N1500A/Opt._005_General_Overview.htm

Validation plan: reinstall the sample; test a second sample of the same composition; include an unfilled-binder control; compare different thicknesses/turn counts where applicable; and, if possible, obtain one independent laboratory reference measurement. Set acceptance limits after assessing uncertainty, rather than arbitrarily based on an attractive curve.

## 6. Initial sample matrix and equipment

Preliminary matrix: unfilled binder; powder A at three volume fractions; powder B at the same three volume fractions. This gives seven formulations only if two justified candidates can be selected; with one candidate, there are four. Select volume fractions after obtaining density and viscosity data and supplier recommendations. For each formulation, make a toroid and a flat sample from the same mixture, with replicates for the most promising formulation.

Log: component masses, true densities, volume-fraction calculation, shape, dimensions, finished sample mass/volume, curing temperature and time, batch number, fixture and calibration, and raw complex measurements. For the future gradient, also record layer order and thicknesses, adhesive, and gaps.

Initial equipment:

- a balance with resolution appropriate for the selected batch mass, and a caliper/micrometer;
- molds for toroids and flat samples, and controlled mixing;
- a binder with known electrical properties and a specified curing schedule;
- extraction ventilation/powder handling according to the SDS; do not use dry spraying;
- vacuum degassing if needed, and a dedicated thermal chamber/oven for curing according to the specification;
- measurement fixtures and access to a suitable instrument, possibly through rental or a laboratory.

The ceramic route additionally requires a press with a suitable die, debinding equipment, a high-temperature furnace with the required atmosphere, refractory fixtures, and shrinkage control. The selected process determines the equipment and schedule; a household oven is not a substitute. Outsourcing this work is preferable at the first stage.

## 7. Gradient design and usefulness criterion

For a rough estimate at low losses, n≈sqrt(εr μr), Z≈377 sqrt(μr/εr) ohms. These scalar expressions require checks of homogeneity and model applicability, especially for a multilayer sample. High μ′ and ε′ accompanied by high μ″/ε″ may primarily produce absorption.

Selection objective: obtain a characterized family of formulations that allows n to vary while controlling the μ/ε ratio. Changing the concentration of one powder does not guarantee the required trajectory. A different ferrite, a dielectric filler, or a transition to metallic cells may be needed; none of these options should be considered proven in advance.

First gradient test: three separately manufactured homogeneous layers with known properties. Compare against a homogeneous control with the same total thickness and a comparable average composition. The gradient orientation — along or across propagation — must follow the selected electromagnetic field problem. For a guided line, matching to 50 ohms and matching the material impedance to air are different conditions.

The force stage starts only after calculating stresses/forces for the actual sample, accounting for the source, conductors, material, absorber, and boundaries. Sensitivity assessment and controls for heating, mechanical cable forces, and magnetic interactions are required. Power and delay readings alone do not establish pressure or thrust.

## Next concrete deliverable

Prepare a common technical inquiry for PPT and Fair-Rite, then obtain data and sample terms. In parallel, select the measurement method and reference-sample geometry. Then prepare a small, justified purchasing list. Do not purchase a ceramic furnace, press, or large powder batch before this point.
