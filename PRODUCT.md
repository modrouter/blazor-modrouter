# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users
Blazor developers who know the built-in `Router` and are deciding whether to adopt ModRouter. They come to the sample CRM to understand one concept at a time, not to read reference docs. (Inferred from the brief and the repository; not confirmed in an interview.)

## Product Purpose
ModRouter.Crm is the interactive teaching app behind `ModRouter/docs/lessons/`. A guide beside a small CRM turns on one ModRouter feature per chapter, and the lessons walk through the same code. Success: a visitor understands a feature after one chapter without having read anything else.

## Capabilities and Constraints
- Blazor WebAssembly, run locally; the live demo on GitHub Pages is the sample CRM under `/crm/`.
- Lessons quote CRM code; `CrmSnippetTests` fails when a quoted line leaves its file, so the CRM and the lessons change together.
- The route log (stages of the last navigation) is the core teaching device and stays.
- User-facing words stay plain, short and active.

## Brand Commitments
Name: ModRouter Sample CRM. Styling follows the CRM's own Tailwind components in `ModRouter.Crm/Ui/`.

## Product Principles
- One concept per screen; nothing on screen that the lesson does not use.
- Show the router working before explaining it.
- Plain words over router jargon.

## Evidence on Hand
Eight chapters in `ModRouter.Crm/Guide/Chapters.cs` and nine lessons in `ModRouter/docs/lessons/`. No testimonials, usage data or screenshots exist; none are invented.
