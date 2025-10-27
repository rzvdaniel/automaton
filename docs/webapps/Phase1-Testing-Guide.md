# Phase 1 Preview Feature - Testing Guide

## Quick Start

1. **Navigate to Apps**
   ```
   http://localhost:5000/apps
   ```

2. **Create or Edit an App**
   - Click "Create App" or "Edit" on existing app
   - You'll be redirected to `/appdesigner/{appId}`

3. **Switch to Preview Tab**
   - Click on the "Preview" tab (between "Code" and "Settings")
   - You should see your HTML rendered in an iframe

4. **Test the Features**

### Feature 1: Live Preview

**Steps:**
1. Switch to "Code" tab
2. Edit the HTML (e.g., change `<h1>` text)
3. Switch back to "Preview" tab
4. Click "Refresh" button
5. **Expected:** Changes appear in preview

### Feature 2: Open in New Tab

**Steps:**
1. In Preview tab, click "Open in New Tab" button
2. **Expected:** New browser tab opens with your app
3. Open browser console (F12)
4. **Expected:** See "? Automaton bridge loaded (Preview Mode)"

### Feature 3: Automaton Bridge (Preview Mode)

**Steps:**
1. Add this code to your HTML in Monaco editor:

```html
<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
    <title>Test App</title>
</head>
<body>
    <h1>Test Automaton Bridge</h1>
    <button onclick="testFlow()">Test Flow Execution</button>
    <div id="result"></div>

    <script>
        async function testFlow() {
 console.log('Testing Automaton.executeFlow...');
            
            const result = await Automaton.executeFlow('test-flow-id', {
      testInput: 'Hello from preview!'
    });
    
   console.log('Result:', result);
            document.getElementById('result').innerHTML = 
    `<pre>${JSON.stringify(result, null, 2)}</pre>`;
        }
    </script>
</body>
</html>
```

2. Switch to Preview tab
3. Click "Refresh"
4. Click the "Test Flow Execution" button in the preview
5. **Expected Results:**
   - Console shows: `?? Preview Mode: Automaton.executeFlow called`
   - Console shows: `Flow ID: test-flow-id`
   - Console shows: `Inputs: {testInput: "Hello from preview!"}`
   - Preview shows mock response:
     ```json
     {
       "success": true,
       "message": "Preview mode - flow not executed",
       "data": {
         "timestamp": "2024-01-15T10:30:00.000Z",
         "previewMode": true
 }
     }
     ```

---

## Test Scenarios

### Scenario 1: Basic HTML Preview

**HTML:**
```html
<!DOCTYPE html>
<html>
<head>
    <title>Hello World</title>
    <style>
        body { font-family: Arial; padding: 20px; }
  h1 { color: #1890ff; }
    </style>
</head>
<body>
    <h1>Hello, World!</h1>
    <p>This is a preview test.</p>
</body>
</html>
```

**Expected:** Page renders with blue heading and paragraph

---

### Scenario 2: Interactive JavaScript

**HTML:**
```html
<!DOCTYPE html>
<html>
<head>
    <title>Counter App</title>
</head>
<body>
    <h1>Counter: <span id="count">0</span></h1>
    <button onclick="increment()">Increment</button>
    <button onclick="reset()">Reset</button>

    <script>
      let count = 0;
        
        function increment() {
  count++;
    document.getElementById('count').textContent = count;
        }
      
   function reset() {
          count = 0;
     document.getElementById('count').textContent = count;
    }
    </script>
</body>
</html>
```

**Expected:** 
- Counter starts at 0
- Clicking "Increment" increases counter
- Clicking "Reset" sets counter to 0

---

### Scenario 3: CSS Animation

**HTML:**
```html
<!DOCTYPE html>
<html>
<head>
    <title>Animation Test</title>
    <style>
 @keyframes pulse {
            0%, 100% { transform: scale(1); }
        50% { transform: scale(1.1); }
     }
 
        .box {
 width: 100px;
            height: 100px;
  background: #1890ff;
  margin: 50px;
            animation: pulse 2s infinite;
        }
    </style>
</head>
<body>
    <div class="box"></div>
</body>
</html>
```

**Expected:** Blue box pulses (grows/shrinks) continuously

---

### Scenario 4: Form Submission (Mock)

**HTML:**
```html
<!DOCTYPE html>
<html>
<head>
 <title>Contact Form</title>
    <style>
 form { max-width: 400px; margin: 50px; }
        input, textarea { width: 100%; margin: 10px 0; padding: 8px; }
        button { padding: 10px 20px; background: #1890ff; color: white; border: none; }
    </style>
</head>
<body>
    <form onsubmit="submitForm(event)">
        <h2>Contact Us</h2>
     <input type="text" id="name" placeholder="Name" required>
        <input type="email" id="email" placeholder="Email" required>
        <textarea id="message" placeholder="Message" rows="4" required></textarea>
   <button type="submit">Submit</button>
    </form>
    <div id="result"></div>

    <script>
   async function submitForm(e) {
            e.preventDefault();

          const result = await Automaton.executeFlow('contact-form-flow', {
      name: document.getElementById('name').value,
     email: document.getElementById('email').value,
      message: document.getElementById('message').value
            });
     
      document.getElementById('result').innerHTML = 
           `<p style="color: green;">? ${result.message}</p>`;
        }
    </script>
</body>
</html>
```

**Expected:** 
- Form validates inputs
- Submit button calls Automaton.executeFlow
- Mock success message appears
- Console shows preview mode logs

---

## Troubleshooting

### Issue: Preview is blank

**Possible Causes:**
1. HTML syntax error
2. Missing `<html>` or `<body>` tags
3. JavaScript error breaking execution

**Solution:**
- Open browser console (F12)
- Check for error messages
- Fix syntax errors in HTML

### Issue: Automaton bridge not found

**Possible Causes:**
1. Bridge injection failed
2. Script tag missing/malformed

**Solution:**
- Check browser console for `Automaton` object
- Run `console.log(window.Automaton)` in console
- Verify script tag is present in iframe source

### Issue: Preview doesn't update after editing

**Solution:**
- Click "Refresh" button in Preview toolbar
- Check that Monaco editor has the new code
- Try switching tabs (Code ? Preview)

---

## Browser DevTools Tips

### Check Bridge Injection

```javascript
// In browser console (within preview iframe):
console.log(window.Automaton);
// Should show: {executeFlow: ƒ, version: "1.0.0", mode: "preview"}
```

### Test executeFlow Manually

```javascript
// In browser console (within preview iframe):
Automaton.executeFlow('test', {foo: 'bar'}).then(console.log);
// Should log mock response
```

### Inspect Iframe Content

1. Right-click in preview area
2. Select "Inspect" or "Inspect Element"
3. Navigate to `<iframe id="app-preview-frame">`
4. View `srcdoc` attribute to see injected HTML

---

## Performance Testing

### Measure Preview Load Time

```javascript
// Add to your HTML:
<script>
    const start = performance.now();
    window.addEventListener('load', () => {
        const loadTime = performance.now() - start;
  console.log(`Preview loaded in ${loadTime.toFixed(2)}ms`);
    });
</script>
```

**Expected:** < 500ms (usually 100-200ms)

### Measure Refresh Time

1. Open browser console
2. Click "Refresh" button
3. Note timestamp before and after
4. **Expected:** < 500ms

---

## Known Limitations

1. **Same-Origin Policy**: Preview can only call APIs on same origin (by design)
2. **No Popups**: Sandbox prevents `window.open()` popups (except new tabs from toolbar)
3. **No Top Navigation**: Sandbox prevents navigating parent window
4. **Mock Responses**: All `Automaton.executeFlow()` calls return mock data in preview

These limitations are intentional for security and preview isolation.

---

## Success Indicators

? **Phase 1 is working correctly if:**
1. Preview tab is visible and clickable
2. HTML renders in iframe without errors
3. JavaScript executes (console.log works)
4. CSS styles apply correctly
5. "Refresh" button updates preview
6. "Open in New Tab" opens separate window
7. `Automaton.executeFlow()` returns mock responses
8. Console shows "Preview Mode" messages
9. No console errors about missing Automaton bridge
10. Iframe sandbox prevents dangerous operations

---

## Reporting Issues

If you encounter issues:

1. **Check Console**: Open browser DevTools (F12) and check for errors
2. **Screenshot**: Take screenshot of the issue
3. **HTML Sample**: Include minimal HTML that reproduces issue
4. **Browser Info**: Specify browser and version
5. **Steps to Reproduce**: List exact steps that cause the issue

**Create Issue:**
- GitHub issue with label `web-apps-preview`
- Title: `[Preview] Brief description of issue`
- Include all information from above

---

## Next Steps After Testing

Once Phase 1 is validated:
1. Move to Phase 2: Publishing Mechanism
2. Implement database changes for `PublishedAt` and `Version`
3. Create publish/unpublish endpoints
4. Add "View Published" functionality (currently shows warning)

---

**Happy Testing! ??**

For questions: Reference `docs/Phase1-PreviewImplementation-Summary.md`
