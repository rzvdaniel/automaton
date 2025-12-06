# Example Web Apps & Custom Steps Guide

This document provides AI prompts for generating example Web Apps and their corresponding Custom Steps (backend logic). These examples can be used for:

1. **New User Onboarding** - Pre-built examples to help users learn the platform
2. **Developer Templates** - Starting points for building monetizable apps

---

## Table of Contents

- [Starter Examples](#starter-examples)
  - [1. QR Code Generator](#1-qr-code-generator)
  - [2. JSON Formatter/Validator](#2-json-formattervalidator)
  - [3. Password Generator](#3-password-generator)
  - [4. URL Shortener Demo](#4-url-shortener-demo)
  - [5. Unit Converter](#5-unit-converter)
  - [6. Markdown to HTML Converter](#6-markdown-to-html-converter)
  - [7. Color Palette Generator](#7-color-palette-generator)
- [Advanced/Monetizable Apps](#advancedmonetizable-apps)
  - [8. AI Content Writer](#8-ai-content-writer)
  - [9. SEO Meta Generator](#9-seo-meta-generator)
  - [10. Invoice Generator](#10-invoice-generator)
  - [11. Image Resizer/Compressor](#11-image-resizercompressor)
  - [12. Data Dashboard Builder](#12-data-dashboard-builder)
  - [13. Email Template Builder](#13-email-template-builder)
  - [14. API Health Monitor](#14-api-health-monitor)
- [Usage Instructions](#usage-instructions)

---

## Starter Examples

These examples are ideal for new users learning the platform. They demonstrate core capabilities without requiring external API keys.

---

### 1. QR Code Generator

**Description:** Generate QR codes from any text or URL input.

#### App AI Prompt

```
Create a modern QR Code Generator web app with:
- Clean dark theme with a centered card layout
- Input field for text/URL with placeholder "Enter text or URL"
- A "Generate QR Code" button with loading state
- Display area for the generated QR code image (base64)
- Copy button to copy the QR code as image
- Download button to save QR code as PNG
- Error handling with user-friendly messages
- Use window.automatonApi.executeFlow('generate-qr-code', { inputs: { text: inputValue }}) to call the backend
- Parse outputVariables.qrCodeImage from the response to display the base64 image
- Responsive design that works on mobile
```

#### Custom Step AI Prompt (Python - GenerateQRCode)

```
Create a Python custom step that generates a QR code image.

Input variables:
- text (string): The text or URL to encode in the QR code

Output variables:
- qrCodeImage (string): Base64-encoded PNG image of the QR code

Requirements:
- Use the qrcode library with PIL/Pillow for image generation
- Generate a 256x256 pixel QR code
- Use high error correction (ERROR_CORRECT_H)
- Convert to base64 string with "data:image/png;base64," prefix
- Handle empty input gracefully
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `text`        | string | Text or URL to encode          |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `qrCodeImage`  | string | Base64 PNG with data URI prefix |

---

### 2. JSON Formatter/Validator

**Description:** Paste raw JSON to validate, format, and prettify it with syntax highlighting.

#### App AI Prompt

```
Create a JSON Formatter and Validator web app with:
- Split panel layout: left for input, right for output
- Dark theme with syntax highlighting colors
- Textarea for pasting raw JSON (left side)
- Formatted output display with line numbers (right side)
- "Format" button and "Validate" button
- Toggle for compact vs pretty output (2 or 4 space indent)
- Copy formatted output button
- Error display showing line/column of JSON errors
- Red highlight on invalid JSON, green checkmark for valid
- Use window.automatonApi.executeFlow('format-json', { inputs: { jsonText: input, indent: indentSize }})
- Display validation errors from outputVariables.errors
- Show formatted JSON from outputVariables.formatted
```

#### Custom Step AI Prompt (Python - FormatJSON)

```
Create a Python custom step that validates and formats JSON text.

Input variables:
- jsonText (string): Raw JSON text to validate and format
- indent (number): Number of spaces for indentation (default 2)

Output variables:
- formatted (string): Pretty-printed JSON if valid
- isValid (boolean): True if JSON is valid
- errors (string): Error message with line/column if invalid, empty if valid

Requirements:
- Parse the JSON and catch any JSONDecodeError
- Extract line and column from parse errors
- Format with specified indentation
- Preserve unicode characters properly
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `jsonText`    | string | Raw JSON to format             |
| `indent`      | number | Spaces for indentation (2 or 4)|

| Output Variable | Type    | Description                    |
|----------------|---------|--------------------------------|
| `formatted`    | string  | Pretty-printed JSON            |
| `isValid`      | boolean | Validation result              |
| `errors`       | string  | Error details if invalid       |

---

### 3. Password Generator

**Description:** Generate cryptographically secure passwords with customizable options.

#### App AI Prompt

```
Create a secure Password Generator web app with:
- Modern card-based dark UI
- Large display area showing the generated password with monospace font
- Slider for password length (8-64 characters, default 16)
- Checkboxes for: Uppercase, Lowercase, Numbers, Symbols
- "Generate" button with refresh icon
- One-click copy button with "Copied!" feedback
- Password strength indicator bar (weak/medium/strong/very strong)
- History of last 5 generated passwords (session only, no storage)
- Use window.automatonApi.executeFlow('generate-password', { inputs: { length, uppercase, lowercase, numbers, symbols }})
- Display password from outputVariables.password
- Enable as PWA for easy access
```

#### Custom Step AI Prompt (Python - GeneratePassword)

```
Create a Python custom step that generates a cryptographically secure password.

Input variables:
- length (number): Password length (8-64)
- uppercase (boolean): Include A-Z
- lowercase (boolean): Include a-z  
- numbers (boolean): Include 0-9
- symbols (boolean): Include !@#$%^&*()_+-=[]{}|;:,.<>?

Output variables:
- password (string): The generated password
- strength (string): Password strength rating (weak/medium/strong/very strong)
- entropy (number): Bits of entropy

Requirements:
- Use Python's secrets module for cryptographic randomness
- Ensure at least one character from each selected category
- Calculate entropy based on character pool size
- Validate at least one category is selected
```

#### Flow Setup

| Input Variable | Type    | Description                    |
|---------------|---------|--------------------------------|
| `length`      | number  | Password length (8-64)         |
| `uppercase`   | boolean | Include uppercase letters      |
| `lowercase`   | boolean | Include lowercase letters      |
| `numbers`     | boolean | Include digits                 |
| `symbols`     | boolean | Include special characters     |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `password`     | string | Generated password             |
| `strength`     | string | Strength rating                |
| `entropy`      | number | Bits of entropy                |

---

### 4. URL Shortener Demo

**Description:** Demonstrate flow I/O with a URL shortening simulation.

#### App AI Prompt

```
Create a URL Shortener demo web app with:
- Minimalist design with gradient accent color
- Large input field for long URL with validation
- "Shorten" button with loading animation
- Display shortened URL with one-click copy
- QR code preview of the shortened URL
- Click counter display (simulated)
- List of recently shortened URLs (session storage)
- Use window.automatonApi.executeFlow('shorten-url', { inputs: { url: longUrl }})
- Display short code from outputVariables.shortCode
- Note: This is a demo - explain URLs work only within this session
```

#### Custom Step AI Prompt (Python - ShortenURL)

```
Create a Python custom step that generates a short URL code.

Input variables:
- url (string): The long URL to shorten

Output variables:
- shortCode (string): 6-character alphanumeric code
- shortUrl (string): Full short URL path
- isValid (boolean): Whether input was a valid URL

Requirements:
- Validate the URL format (must start with http:// or https://)
- Generate a 6-character code using base62 encoding (a-z, A-Z, 0-9)
- Use hash of URL + timestamp for uniqueness
- Return the short code and constructed short URL
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `url`         | string | Long URL to shorten            |

| Output Variable | Type    | Description                    |
|----------------|---------|--------------------------------|
| `shortCode`    | string  | 6-character code               |
| `shortUrl`     | string  | Full shortened URL path        |
| `isValid`      | boolean | URL validation result          |

---

### 5. Unit Converter

**Description:** Convert between common units for length, weight, temperature, volume, and time.

#### App AI Prompt

```
Create a Unit Converter web app with:
- Clean, calculator-style interface with dark theme
- Category tabs: Length, Weight, Temperature, Volume, Time
- Two dropdowns for "From" and "To" units
- Numeric input field with the value to convert
- Real-time conversion result display (large, bold)
- Swap button to reverse From/To units
- Common conversion shortcuts (e.g., km to miles, kg to lbs)
- Formula display showing the conversion equation
- Use window.automatonApi.executeFlow('convert-units', { inputs: { value, fromUnit, toUnit, category }})
- Display result from outputVariables.result and formula from outputVariables.formula
- Enable as PWA
```

#### Custom Step AI Prompt (Python - ConvertUnits)

```
Create a Python custom step that converts between units.

Input variables:
- value (number): The numeric value to convert
- fromUnit (string): Source unit (e.g., "km", "celsius", "kg")
- toUnit (string): Target unit (e.g., "miles", "fahrenheit", "lbs")
- category (string): Category (length/weight/temperature/volume/time)

Output variables:
- result (number): Converted value
- formula (string): Human-readable conversion formula used
- precision (number): Decimal places in result

Requirements:
- Support common units for each category
- Length: mm, cm, m, km, in, ft, yd, mi
- Weight: mg, g, kg, oz, lb
- Temperature: celsius, fahrenheit, kelvin
- Volume: ml, l, gal, cup, tbsp, tsp
- Time: ms, s, min, hr, day
- Return formula like "1 km = 0.621371 miles"
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `value`       | number | Value to convert               |
| `fromUnit`    | string | Source unit                    |
| `toUnit`      | string | Target unit                    |
| `category`    | string | Unit category                  |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `result`       | number | Converted value                |
| `formula`      | string | Conversion formula used        |
| `precision`    | number | Decimal places                 |

---

### 6. Markdown to HTML Converter

**Description:** Convert Markdown text to formatted HTML with live preview.

#### App AI Prompt

```
Create a Markdown to HTML converter web app with:
- Side-by-side editor layout (50/50 split)
- Left: Monaco-style textarea for Markdown input with line numbers
- Right: Live HTML preview with proper styling
- Toolbar with common Markdown buttons (bold, italic, link, image, code, list)
- Tab to view raw HTML output
- Copy HTML button
- Dark theme with GitHub-flavored Markdown styling for preview
- Sample Markdown loaded by default
- Use window.automatonApi.executeFlow('convert-markdown', { inputs: { markdown: text }})
- Update preview from outputVariables.html on each change (debounced 500ms)
```

#### Custom Step AI Prompt (Python - ConvertMarkdown)

```
Create a Python custom step that converts Markdown to HTML.

Input variables:
- markdown (string): Markdown text to convert

Output variables:
- html (string): Converted HTML
- plainText (string): Text content without formatting
- wordCount (number): Number of words in content

Requirements:
- Use the markdown library with extensions: tables, fenced_code, codehilite
- Sanitize output to prevent XSS (remove script tags)
- Support GitHub-flavored Markdown syntax
- Count words from plain text extraction
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `markdown`    | string | Markdown text to convert       |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `html`         | string | Converted HTML                 |
| `plainText`    | string | Plain text content             |
| `wordCount`    | number | Word count                     |

---

### 7. Color Palette Generator

**Description:** Generate harmonious color palettes from a base color.

#### App AI Prompt

```
Create a Color Palette Generator web app with:
- Large color picker or hex input for base color
- Display 5-color harmonious palette (primary + 4 variations)
- Palette types dropdown: Complementary, Analogous, Triadic, Monochromatic
- Each color shows: hex, RGB, HSL values
- Click any color to copy hex code
- Download palette as JSON or CSS variables
- Preview section showing palette applied to UI elements (buttons, cards, text)
- Dark/light background toggle to test contrast
- Use window.automatonApi.executeFlow('generate-palette', { inputs: { baseColor, paletteType }})
- Display colors array from outputVariables.colors
```

#### Custom Step AI Prompt (Python - GenerateColorPalette)

```
Create a Python custom step that generates a color palette.

Input variables:
- baseColor (string): Hex color code (e.g., "#3498db")
- paletteType (string): Type of palette (complementary/analogous/triadic/monochromatic)

Output variables:
- colors (string): JSON array of 5 color objects with hex, rgb, hsl values
- cssVariables (string): CSS custom properties for the palette

Requirements:
- Parse hex to HSL for color math
- Complementary: opposite on color wheel (180°)
- Analogous: adjacent colors (±30°)
- Triadic: three evenly spaced (120° apart)
- Monochromatic: same hue, varying saturation/lightness
- Return array of objects: [{hex, rgb: {r,g,b}, hsl: {h,s,l}}]
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `baseColor`   | string | Hex color code                 |
| `paletteType` | string | Palette type                   |

| Output Variable  | Type   | Description                    |
|-----------------|--------|--------------------------------|
| `colors`        | string | JSON array of color objects    |
| `cssVariables`  | string | CSS custom properties          |

---

## Advanced/Monetizable Apps

These templates are designed for developers building apps with subscription potential. Many require AI API keys.

---

### 8. AI Content Writer

**Description:** Generate blog posts, social media content, and marketing copy using AI.

**Monetization:** Credits-based subscription, pay-per-generation.

#### App AI Prompt

```
Create an AI Content Writer web app with:
- Professional dark theme with sidebar navigation
- Content type selector: Blog Post, Social Media, Email, Product Description
- Input fields: Topic, Keywords, Tone (formal/casual/professional), Length
- "Generate" button with streaming text effect
- Generated content display with word count
- Edit/Regenerate buttons
- Copy and Download (as .txt or .md) buttons
- History sidebar showing recent generations
- Usage counter display (credits remaining)
- Use window.automatonApi.executeFlow('generate-content', { inputs: { contentType, topic, keywords, tone, length }})
- Display generated content from outputVariables.content
```

#### Custom Step AI Prompt (Python - GenerateAIContent)

```
Create a Python custom step that generates content using an AI API.

Input variables:
- contentType (string): Type of content (blog/social/email/product)
- topic (string): Main topic or subject
- keywords (string): Comma-separated keywords to include
- tone (string): Writing tone (formal/casual/professional)
- length (string): Desired length (short/medium/long)
- apiKey (string, sensitive): OpenAI API key

Output variables:
- content (string): Generated content
- wordCount (number): Number of words generated
- tokensUsed (number): API tokens consumed

Requirements:
- Use OpenAI API (gpt-4 or gpt-3.5-turbo)
- Build appropriate system prompt for each content type
- Include keywords naturally in the content
- Return usage metrics for billing
```

#### Flow Setup

| Input Variable | Type            | Description                    |
|---------------|-----------------|--------------------------------|
| `contentType` | string          | Type of content                |
| `topic`       | string          | Main topic                     |
| `keywords`    | string          | Keywords to include            |
| `tone`        | string          | Writing tone                   |
| `length`      | string          | Desired length                 |
| `apiKey`      | string (sensitive) | OpenAI API key              |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `content`      | string | Generated content              |
| `wordCount`    | number | Word count                     |
| `tokensUsed`   | number | API tokens used                |

---

### 9. SEO Meta Generator

**Description:** Generate SEO-optimized meta titles and descriptions from page content.

**Monetization:** Per-use fee or monthly subscription.

#### App AI Prompt

```
Create an SEO Meta Generator web app with:
- Clean, tool-focused interface
- Large textarea for page content/article input
- Target keyword input field
- "Generate Meta" button
- Output section with:
  - Meta Title (with character count, 50-60 optimal)
  - Meta Description (with character count, 150-160 optimal)
  - Focus keyword analysis
  - Suggested alt tags for images
- Preview showing how it appears in Google search results
- Copy individual fields or all as HTML meta tags
- Use window.automatonApi.executeFlow('generate-seo-meta', { inputs: { content, targetKeyword }})
- Display results from outputVariables
```

#### Custom Step AI Prompt (Python - GenerateSEOMeta)

```
Create a Python custom step that generates SEO meta tags using AI.

Input variables:
- content (string): Page content or article text
- targetKeyword (string): Primary SEO keyword
- apiKey (string, sensitive): OpenAI API key

Output variables:
- metaTitle (string): SEO-optimized title (50-60 chars)
- metaDescription (string): SEO-optimized description (150-160 chars)
- keywordDensity (number): Percentage of keyword in content
- suggestedAltTags (string): JSON array of image alt tag suggestions
- htmlMeta (string): Complete HTML meta tags ready to copy

Requirements:
- Analyze content for key themes
- Generate compelling, click-worthy title under 60 characters
- Create description with call-to-action, under 160 characters
- Include target keyword naturally in both
- Calculate keyword density
```

#### Flow Setup

| Input Variable   | Type            | Description                    |
|-----------------|-----------------|--------------------------------|
| `content`       | string          | Page content                   |
| `targetKeyword` | string          | Primary SEO keyword            |
| `apiKey`        | string (sensitive) | OpenAI API key              |

| Output Variable     | Type   | Description                    |
|--------------------|--------|--------------------------------|
| `metaTitle`        | string | SEO title                      |
| `metaDescription`  | string | SEO description                |
| `keywordDensity`   | number | Keyword density %              |
| `suggestedAltTags` | string | JSON array of alt tags         |
| `htmlMeta`         | string | Complete HTML meta tags        |

---

### 10. Invoice Generator

**Description:** Create professional PDF invoices from form data.

**Monetization:** Per-invoice fee or monthly subscription.

#### App AI Prompt

```
Create an Invoice Generator web app with:
- Professional, printable layout design
- Company details section (name, address, logo upload)
- Client details section
- Invoice metadata (number, date, due date)
- Line items table: Description, Quantity, Unit Price, Total
- Add/Remove line item buttons
- Subtotal, Tax rate input, Tax amount, Grand Total calculations
- Notes/Terms textarea
- "Generate PDF" button
- Preview modal showing invoice
- Download as PDF button
- Use window.automatonApi.executeFlow('generate-invoice', { inputs: { invoiceData: JSON.stringify(formData) }})
- Receive PDF as base64 from outputVariables.pdfBase64
```

#### Custom Step AI Prompt (Python - GenerateInvoicePDF)

```
Create a Python custom step that generates a PDF invoice.

Input variables:
- invoiceData (string): JSON string containing invoice details:
  {
    company: {name, address, email, logo},
    client: {name, address, email},
    invoice: {number, date, dueDate},
    items: [{description, quantity, unitPrice}],
    taxRate: number,
    notes: string
  }

Output variables:
- pdfBase64 (string): Base64-encoded PDF file
- totalAmount (number): Invoice total
- fileName (string): Suggested filename

Requirements:
- Use reportlab or weasyprint for PDF generation
- Professional invoice template layout
- Calculate line totals, subtotal, tax, grand total
- Include company logo if provided (base64 image)
- Generate filename as "Invoice-{number}-{date}.pdf"
```

#### Flow Setup

| Input Variable | Type   | Description                    |
|---------------|--------|--------------------------------|
| `invoiceData` | string | JSON string with invoice data  |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `pdfBase64`    | string | Base64-encoded PDF             |
| `totalAmount`  | number | Invoice total                  |
| `fileName`     | string | Suggested filename             |

---

### 11. Image Resizer/Compressor

**Description:** Resize and compress images for web optimization.

**Monetization:** Per-image processing fee or bulk subscription.

#### App AI Prompt

```
Create an Image Resizer and Compressor web app with:
- Drag-and-drop upload zone with file browser fallback
- Image preview (before and after)
- Resize options: Width, Height, Maintain aspect ratio checkbox
- Preset sizes: Thumbnail (150px), Social (1200px), HD (1920px)
- Quality slider (1-100) for compression
- Format selector: JPEG, PNG, WebP
- File size display: Original vs Compressed
- Batch upload support (up to 5 images)
- Download button (individual or ZIP for batch)
- Use window.automatonApi.executeFlow('resize-image', { inputs: { imageBase64, width, height, quality, format }})
- Handle large files with progress indicator
```

#### Custom Step AI Prompt (Python - ResizeImage)

```
Create a Python custom step that resizes and compresses images.

Input variables:
- imageBase64 (string): Base64-encoded input image
- width (number): Target width in pixels (0 for auto)
- height (number): Target height in pixels (0 for auto)
- quality (number): Compression quality 1-100
- format (string): Output format (jpeg/png/webp)
- maintainAspectRatio (boolean): Keep proportions

Output variables:
- outputBase64 (string): Base64-encoded result image
- originalSize (number): Original file size in bytes
- newSize (number): Compressed file size in bytes
- dimensions (string): Final dimensions as "WxH"

Requirements:
- Use Pillow (PIL) for image processing
- Support common input formats (JPEG, PNG, GIF, WebP, BMP)
- Calculate new dimensions maintaining aspect ratio if enabled
- Optimize compression for web use
- Strip EXIF data for privacy
```

#### Flow Setup

| Input Variable        | Type    | Description                    |
|----------------------|---------|--------------------------------|
| `imageBase64`        | string  | Base64 input image             |
| `width`              | number  | Target width                   |
| `height`             | number  | Target height                  |
| `quality`            | number  | Compression quality (1-100)    |
| `format`             | string  | Output format                  |
| `maintainAspectRatio`| boolean | Keep proportions               |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `outputBase64` | string | Base64 result image            |
| `originalSize` | number | Original size in bytes         |
| `newSize`      | number | New size in bytes              |
| `dimensions`   | string | Final dimensions               |

---

### 12. Data Dashboard Builder

**Description:** Connect to APIs and visualize data with charts and widgets.

**Monetization:** Team/enterprise subscription tiers.

#### App AI Prompt

```
Create a Data Dashboard Builder web app with:
- Dashboard canvas with grid layout
- Widget palette: Line Chart, Bar Chart, Pie Chart, Stat Card, Table
- Drag widgets onto canvas, resize handles
- Widget configuration panel for selected widget
- Data source input: API URL or paste JSON
- Field mapper: assign data fields to chart axes/values
- Auto-refresh interval setting (30s, 1m, 5m, off)
- Dashboard title and description
- Save/Load dashboard configuration
- Full-screen presentation mode
- Use Chart.js for visualizations
- Use window.automatonApi.executeFlow('fetch-dashboard-data', { inputs: { apiUrl, apiKey }}) for data fetching
```

#### Custom Step AI Prompt (Python - FetchDashboardData)

```
Create a Python custom step that fetches and transforms data for dashboards.

Input variables:
- apiUrl (string): API endpoint URL to fetch data from
- apiKey (string, sensitive): Optional API key for authentication
- headers (string): Optional JSON string of additional headers
- transform (string): JSONPath or JMESPath expression for data extraction

Output variables:
- data (string): JSON string of fetched and transformed data
- recordCount (number): Number of records
- fetchedAt (string): ISO timestamp of fetch
- error (string): Error message if failed

Requirements:
- Use requests library for HTTP calls
- Support GET requests with optional headers
- Handle JSON response parsing
- Apply optional JSONPath transformation
- Timeout after 30 seconds
- Return meaningful errors for failures
```

#### Flow Setup

| Input Variable | Type            | Description                    |
|---------------|-----------------|--------------------------------|
| `apiUrl`      | string          | API endpoint URL               |
| `apiKey`      | string (sensitive) | Optional API key            |
| `headers`     | string          | JSON headers                   |
| `transform`   | string          | JSONPath expression            |

| Output Variable | Type   | Description                    |
|----------------|--------|--------------------------------|
| `data`         | string | JSON data                      |
| `recordCount`  | number | Number of records              |
| `fetchedAt`    | string | ISO timestamp                  |
| `error`        | string | Error message if failed        |

---

### 13. Email Template Builder

**Description:** Build and preview HTML email templates with variable substitution.

**Monetization:** Monthly subscription for template storage and sending.

#### App AI Prompt

```
Create an Email Template Builder web app with:
- WYSIWYG editor with drag-and-drop blocks
- Block types: Header, Text, Image, Button, Divider, Columns
- Style panel: fonts, colors, spacing, alignment
- Variable insertion: {{firstName}}, {{companyName}}, etc.
- Mobile preview toggle (responsive)
- Template gallery with starter templates
- HTML source view tab
- Test send modal (enter email address)
- Export as HTML button
- Save template with name
- Use window.automatonApi.executeFlow('process-email-template', { inputs: { html, variables }})
- Preview with sample variable values
```

#### Custom Step AI Prompt (Python - ProcessEmailTemplate)

```
Create a Python custom step that processes email templates.

Input variables:
- html (string): Email template HTML with {{variable}} placeholders
- variables (string): JSON object with variable values
- inlineCSS (boolean): Convert style tags to inline styles

Output variables:
- processedHtml (string): HTML with variables replaced
- plainText (string): Plain text version for alt-body
- variablesFound (string): JSON array of variables found in template
- previewHtml (string): HTML with sample data for preview

Requirements:
- Replace {{variableName}} with corresponding values
- Use premailer or similar for CSS inlining if enabled
- Extract plain text version from HTML
- List all variables found in template
- Handle missing variables gracefully (leave placeholder or empty)
```

#### Flow Setup

| Input Variable | Type    | Description                    |
|---------------|---------|--------------------------------|
| `html`        | string  | Template HTML                  |
| `variables`   | string  | JSON variable values           |
| `inlineCSS`   | boolean | Inline CSS styles              |

| Output Variable    | Type   | Description                    |
|-------------------|--------|--------------------------------|
| `processedHtml`   | string | Processed HTML                 |
| `plainText`       | string | Plain text version             |
| `variablesFound`  | string | JSON array of variables        |
| `previewHtml`     | string | Preview with sample data       |

---

### 14. API Health Monitor

**Description:** Monitor API endpoints and display uptime status.

**Monetization:** Per-endpoint monthly fee.

#### App AI Prompt

```
Create an API Health Monitor web app with:
- Dashboard showing all monitored endpoints as status cards
- Each card shows: endpoint name, URL, status (up/down), response time, last checked
- Color coding: green (healthy), yellow (slow), red (down)
- Add endpoint modal: Name, URL, expected status code, check interval
- Historical uptime chart for each endpoint (last 24 hours)
- Aggregate uptime percentage
- Alert configuration (email on status change)
- Manual "Check Now" button per endpoint
- Status page generator (public shareable page)
- Use window.automatonApi.executeFlow('check-endpoint', { inputs: { url, expectedStatus }})
- Poll at configured intervals using setInterval
```

#### Custom Step AI Prompt (Python - CheckEndpoint)

```
Create a Python custom step that checks API endpoint health.

Input variables:
- url (string): API endpoint URL to check
- expectedStatus (number): Expected HTTP status code (default 200)
- timeout (number): Request timeout in seconds (default 10)
- method (string): HTTP method (GET/POST/HEAD, default GET)
- headers (string): Optional JSON headers

Output variables:
- isHealthy (boolean): True if endpoint is healthy
- statusCode (number): HTTP status code received
- responseTime (number): Response time in milliseconds
- error (string): Error message if request failed
- checkedAt (string): ISO timestamp of check

Requirements:
- Use requests with specified timeout
- Measure response time accurately
- Compare status code to expected
- Handle connection errors, timeouts, SSL errors
- Return meaningful error descriptions
```

#### Flow Setup

| Input Variable   | Type   | Description                    |
|-----------------|--------|--------------------------------|
| `url`           | string | Endpoint URL                   |
| `expectedStatus`| number | Expected HTTP status           |
| `timeout`       | number | Timeout in seconds             |
| `method`        | string | HTTP method                    |
| `headers`       | string | JSON headers                   |

| Output Variable | Type    | Description                    |
|----------------|---------|--------------------------------|
| `isHealthy`    | boolean | Health status                  |
| `statusCode`   | number  | HTTP status received           |
| `responseTime` | number  | Response time in ms            |
| `error`        | string  | Error message                  |
| `checkedAt`    | string  | ISO timestamp                  |

---

## Usage Instructions

### Creating a Web App from These Prompts

1. **Navigate to Apps** → Click "Create New App"
2. **Open App Designer** → Click on your new app
3. **Open AI Chat Panel** → Located in the sidebar
4. **Paste the App AI Prompt** → The AI will generate the HTML/CSS/JS code
5. **Review and adjust** → Modify the generated code as needed
6. **Save** → The app is ready to preview (after creating the backend flow)

### Creating a Custom Step from These Prompts

1. **Navigate to Custom Steps** → Click "Create New Step"
2. **Select Language** → Choose Python
3. **Configure Variables**:
   - Add Input Variables as specified in the table
   - Add Output Variables as specified in the table
   - Mark sensitive variables (like `apiKey`) as sensitive
4. **Open AI Chat Panel** → In the Step Designer
5. **Paste the Custom Step AI Prompt** → AI generates the Python code
6. **Test the Step** → Use test values to verify functionality
7. **Save** → The step is ready to use in flows

### Creating the Flow

1. **Navigate to Flows** → Click "Create New Flow"
2. **Name the Flow** → Use the flow name referenced in the app (e.g., `generate-qr-code`)
3. **Add Input Variables** → Match the inputs expected by the app
4. **Add Output Variables** → Match the outputs the app expects
5. **Add the Custom Step** → Drag from the step palette
6. **Map Variables** → Connect flow inputs to step inputs, step outputs to flow outputs
7. **Save and Test** → Run the flow to verify it works
8. **Return to App** → Publish and test the complete integration

### Recommended Starter Examples for New Accounts

For the best onboarding experience, consider auto-creating these 3 examples for new users:

1. **Password Generator** - Simple, no external dependencies, demonstrates PWA
2. **QR Code Generator** - Visual output, demonstrates image generation
3. **JSON Formatter** - Practical tool, demonstrates text processing

These require only Python standard library or common packages (qrcode, Pillow).

---

## Summary Table

| Example | Category | External API | PWA Ready | Monetizable |
|---------|----------|--------------|-----------|-------------|
| QR Code Generator | Starter | No | Yes | No |
| JSON Formatter | Starter | No | No | No |
| Password Generator | Starter | No | Yes | No |
| URL Shortener | Starter | No | No | No |
| Unit Converter | Starter | No | Yes | No |
| Markdown to HTML | Starter | No | No | No |
| Color Palette | Starter | No | No | No |
| AI Content Writer | Advanced | Yes (OpenAI) | No | Yes |
| SEO Meta Generator | Advanced | Yes (OpenAI) | No | Yes |
| Invoice Generator | Advanced | No | No | Yes |
| Image Resizer | Advanced | No | No | Yes |
| Data Dashboard | Advanced | Optional | No | Yes |
| Email Template Builder | Advanced | No | No | Yes |
| API Health Monitor | Advanced | No | No | Yes |
