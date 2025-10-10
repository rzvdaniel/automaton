// Automaton Website JavaScript

// Smooth scrolling for navigation links
document.addEventListener('DOMContentLoaded', function() {
    // Handle smooth scrolling for anchor links
    const anchorLinks = document.querySelectorAll('a[href^="#"]');
    
    anchorLinks.forEach(link => {
        link.addEventListener('click', function(e) {
            e.preventDefault();
            
            const targetId = this.getAttribute('href');
            
            // If href is just "#", scroll to top
            if (targetId === '#') {
                window.scrollTo({
                    top: 0,
                    behavior: 'smooth'
                });
                return;
            }
            
            const targetSection = document.querySelector(targetId);
            
            if (targetSection) {
                const navHeight = document.querySelector('.navbar').offsetHeight;
                const targetPosition = targetSection.offsetTop - navHeight;
                
                window.scrollTo({
                    top: targetPosition,
                    behavior: 'smooth'
                });
            }
        });
    });
    
    // Mobile menu toggle
    const mobileMenuToggle = document.querySelector('.mobile-menu-toggle');
    const navMenu = document.querySelector('.nav-menu');
    
    if (mobileMenuToggle && navMenu) {
        mobileMenuToggle.addEventListener('click', function() {
            navMenu.classList.toggle('active');
            this.classList.toggle('active');
        });
    }
    
    // Navbar background on scroll
    const navbar = document.querySelector('.navbar');
    
    window.addEventListener('scroll', function() {
        if (window.scrollY > 100) {
            navbar.classList.add('scrolled');
        } else {
            navbar.classList.remove('scrolled');
        }
    });
    
    // Intersection Observer for animations
    const observerOptions = {
        threshold: 0.1,
        rootMargin: '0px 0px -50px 0px'
    };
    
    const observer = new IntersectionObserver(function(entries) {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                entry.target.classList.add('animate-in');
            }
        });
    }, observerOptions);
    
    // Observe elements for animation
    const animateElements = document.querySelectorAll('.feature-card, .use-case, .doc-preview, .step-card, .app-element');
    animateElements.forEach(el => observer.observe(el));
    
    // Code editor typing effect
    const codeLines = document.querySelectorAll('.code-line .code-text');
    let delay = 0;
    
    codeLines.forEach((line, index) => {
        setTimeout(() => {
            line.style.animation = 'typeWriter 1s ease forwards';
        }, delay);
        delay += 200;
    });
    
    // Workflow diagram animation
    const flowNodes = document.querySelectorAll('.flow-node');
    const connectors = document.querySelectorAll('.flow-connector');
    
    setTimeout(() => {
        flowNodes.forEach((node, index) => {
            setTimeout(() => {
                node.style.animation = 'slideInScale 0.5s ease forwards';
            }, index * 150);
        });
        
        connectors.forEach((connector, index) => {
            setTimeout(() => {
                connector.style.animation = 'growHeight 0.3s ease forwards';
            }, (index + 1) * 150);
        });
    }, 1000);
    
    // Stats counter animation
    const stats = document.querySelectorAll('.stat-number');
    const statsSection = document.querySelector('.hero-stats');
    
    const statsObserver = new IntersectionObserver(function(entries) {
        entries.forEach(entry => {
            if (entry.isIntersecting) {
                animateStats();
                statsObserver.unobserve(entry.target);
            }
        });
    }, { threshold: 0.5 });
    
    if (statsSection) {
        statsObserver.observe(statsSection);
    }
    
    function animateStats() {
        stats.forEach(stat => {
            const text = stat.textContent.trim();
            if (text === '10x') {
                animateNumber(stat, 1, 10, 'x');
            } else if (text === '100%') {
                animateNumber(stat, 0, 100, '%');
            } else if (text === 'AI' || text === '2-in-1' || text === 'Zero') {
                stat.style.animation = 'pulse 0.8s ease';
            }
        });
    }
    
    function animateNumber(element, start, end, suffix = '') {
        const duration = 1000;
        const startTime = Date.now();
        
        function update() {
            const elapsed = Date.now() - startTime;
            const progress = Math.min(elapsed / duration, 1);
            const current = Math.round(start + (end - start) * easeOutCubic(progress));
            
            element.textContent = current + suffix;
            
            if (progress < 1) {
                requestAnimationFrame(update);
            }
        }
        
        update();
    }
    
    function easeOutCubic(t) {
        return 1 - Math.pow(1 - t, 3);
    }
    
    // Parallax effect for hero background
    window.addEventListener('scroll', function() {
        const scrolled = window.pageYOffset;
        const hero = document.querySelector('.hero');
        
        if (hero) {
            const rate = scrolled * -0.5;
            hero.style.transform = `translateY(${rate}px)`;
        }
    });
    
    // Documentation tabs interaction
    const docItems = document.querySelectorAll('.doc-item');
    const docContent = document.querySelector('.doc-content-preview');
    
    const docContents = {
        'Quick Start': {
            title: '# Quick Start Guide',
            content: `Welcome to Automaton! This guide will help you get started with the automation platform quickly.

## Creating Your First Flow

Let's create a simple "Hello World" automation:`,
            code: `# Navigate to Flows
Click "Flows" in navigation
Click "Create New Flow"`
        },
        'Platform Pages': {
            title: '# Platform Pages',
            content: `This guide provides an overview of the Automaton platform interface and what you can accomplish on each page.

## Main Navigation

### Dashboard (Home)
The main landing page provides an overview of your automation environment:`,
            code: `# Dashboard Features
- Quick Stats: Summary of flows and runners
- Recent Flows: Recently executed workflows
- Runner Status: Connected agents overview`
        },
        'Flows': {
            title: '# Working with Flows',
            content: `Flows are the heart of Automaton - they represent your automation workflows as visual, connected diagrams.

## Understanding Flows

A Flow is a sequence of connected steps that perform automation tasks:`,
            code: `# Flow Components
Start Node → Processing Steps → End Node
Variables pass data between steps
Conditions control execution flow`
        },
        'Runners': {
            title: '# Managing Runners',
            content: `Runners are the execution agents that run your automation flows. They connect to the Automaton platform and execute your workflows.

## What are Runners?

A Runner is a lightweight agent that executes flows on demand:`,
            code: `# Runner Types
Console Runner: Cross-platform CLI
GUI Runner: Windows desktop app
Cloud Runner: Hosted execution`
        }
    };
    
    docItems.forEach(item => {
        item.addEventListener('click', function() {
            // Remove active class from all items
            docItems.forEach(i => i.classList.remove('active'));
            
            // Add active class to clicked item
            this.classList.add('active');
            
            // Update content
            const contentKey = this.textContent;
            const content = docContents[contentKey];
            
            if (content && docContent) {
                docContent.innerHTML = `
                    <h4>${content.title}</h4>
                    <p>${content.content}</p>
                    <div class="code-snippet">
                        ${content.code.split('\n').map(line => 
                            line.startsWith('#') 
                                ? `<span class="code-comment">${line}</span>` 
                                : line.includes('"') 
                                    ? line.replace(/"([^"]+)"/g, '<span class="code-string">"$1"</span>')
                                    : `<span class="code-keyword">${line}</span>`
                        ).join('<br>')}
                    </div>
                `;
            }
        });
    });
});

// Add CSS animations via JavaScript
const style = document.createElement('style');
style.textContent = `
    @keyframes typeWriter {
        from { opacity: 0; transform: translateX(-10px); }
        to { opacity: 1; transform: translateX(0); }
    }
    
    @keyframes slideInScale {
        from { opacity: 0; transform: scale(0.8) translateY(20px); }
        to { opacity: 1; transform: scale(1) translateY(0); }
    }
    
    @keyframes growHeight {
        from { height: 0; opacity: 0; }
        to { height: 1.5rem; opacity: 1; }
    }
    
    @keyframes pulse {
        0%, 100% { transform: scale(1); }
        50% { transform: scale(1.1); }
    }
    
    .navbar.scrolled {
        background: rgba(15, 23, 42, 0.98);
        box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
    }
    
    .animate-in {
        animation: fadeInUp 0.6s ease forwards;
    }
    
    .feature-card,
    .use-case,
    .doc-preview {
        opacity: 0;
        transform: translateY(30px);
    }
    
    .feature-card.animate-in,
    .use-case.animate-in,
    .doc-preview.animate-in {
        opacity: 1;
        transform: translateY(0);
    }
    
    @media (max-width: 768px) {
        .nav-menu.active {
            display: flex;
            position: absolute;
            top: 100%;
            left: 0;
            right: 0;
            background: rgba(15, 23, 42, 0.98);
            flex-direction: column;
            padding: 1rem;
            border-top: 1px solid var(--dark-border);
            backdrop-filter: blur(10px);
        }
        
        .mobile-menu-toggle.active span:nth-child(1) {
            transform: rotate(45deg) translate(6px, 6px);
        }
        
        .mobile-menu-toggle.active span:nth-child(2) {
            opacity: 0;
        }
        
        .mobile-menu-toggle.active span:nth-child(3) {
            transform: rotate(-45deg) translate(6px, -6px);
        }
    }
`;
document.head.appendChild(style);