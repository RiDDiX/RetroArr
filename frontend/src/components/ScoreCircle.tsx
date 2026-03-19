import React, { useEffect, useState } from 'react';
import './ScoreCircle.css';

interface ScoreCircleProps {
  score: number;
  maxScore?: number;
  size?: 'small' | 'medium' | 'large';
  label?: string;
  showLabel?: boolean;
  animated?: boolean;
  onClick?: () => void;
  href?: string;
}

const ScoreCircle: React.FC<ScoreCircleProps> = ({
  score,
  maxScore = 100,
  size = 'medium',
  label,
  showLabel = true,
  animated = true,
  onClick,
  href
}) => {
  const [animatedScore, setAnimatedScore] = useState(animated ? 0 : score);
  
  // Normalize score to percentage
  const normalizedScore = Math.min(100, Math.max(0, (score / maxScore) * 100));
  
  // Calculate circle properties
  const sizeMap = {
    small: { diameter: 48, strokeWidth: 4, fontSize: 12, labelSize: 8 },
    medium: { diameter: 64, strokeWidth: 5, fontSize: 16, labelSize: 10 },
    large: { diameter: 80, strokeWidth: 6, fontSize: 20, labelSize: 12 }
  };
  
  const { diameter, strokeWidth, fontSize, labelSize } = sizeMap[size];
  const radius = (diameter - strokeWidth) / 2;
  const circumference = 2 * Math.PI * radius;
  const strokeDashoffset = circumference - (animatedScore / 100) * circumference;
  
  // Determine color based on score (Metacritic-style)
  const getScoreColor = (s: number): string => {
    if (s >= 75) return 'var(--ctp-green)'; // Green - Great
    if (s >= 50) return 'var(--ctp-yellow)'; // Yellow - Mixed
    return 'var(--ctp-red)'; // Red - Bad
  };
  
  const getScoreGradient = (s: number): string => {
    if (s >= 75) return 'url(#scoreGradientGreen)';
    if (s >= 50) return 'url(#scoreGradientYellow)';
    return 'url(#scoreGradientRed)';
  };
  
  // Animation effect
  useEffect(() => {
    if (!animated) {
      setAnimatedScore(normalizedScore);
      return;
    }
    
    const duration = 1000; // 1 second
    const steps = 60;
    const stepDuration = duration / steps;
    const increment = normalizedScore / steps;
    let currentStep = 0;
    
    const timer = setInterval(() => {
      currentStep++;
      if (currentStep >= steps) {
        setAnimatedScore(normalizedScore);
        clearInterval(timer);
      } else {
        setAnimatedScore(Math.min(normalizedScore, increment * currentStep));
      }
    }, stepDuration);
    
    return () => clearInterval(timer);
  }, [normalizedScore, animated]);
  
  const content = (
    <div 
      className={`score-circle score-circle--${size}`}
      onClick={onClick}
      style={{ cursor: onClick || href ? 'pointer' : 'default' }}
    >
      <svg 
        width={diameter} 
        height={diameter} 
        viewBox={`0 0 ${diameter} ${diameter}`}
        className="score-circle__svg"
      >
        <defs>
          <linearGradient id="scoreGradientGreen" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="var(--ctp-green)" />
            <stop offset="100%" stopColor="#40a02b" />
          </linearGradient>
          <linearGradient id="scoreGradientYellow" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="var(--ctp-yellow)" />
            <stop offset="100%" stopColor="#df8e1d" />
          </linearGradient>
          <linearGradient id="scoreGradientRed" x1="0%" y1="0%" x2="100%" y2="100%">
            <stop offset="0%" stopColor="var(--ctp-red)" />
            <stop offset="100%" stopColor="#d20f39" />
          </linearGradient>
          <filter id="scoreGlow">
            <feGaussianBlur stdDeviation="2" result="coloredBlur"/>
            <feMerge>
              <feMergeNode in="coloredBlur"/>
              <feMergeNode in="SourceGraphic"/>
            </feMerge>
          </filter>
        </defs>
        
        {/* Background circle */}
        <circle
          className="score-circle__bg"
          cx={diameter / 2}
          cy={diameter / 2}
          r={radius}
          strokeWidth={strokeWidth}
        />
        
        {/* Progress circle */}
        <circle
          className="score-circle__progress"
          cx={diameter / 2}
          cy={diameter / 2}
          r={radius}
          strokeWidth={strokeWidth}
          stroke={getScoreGradient(normalizedScore)}
          strokeDasharray={circumference}
          strokeDashoffset={strokeDashoffset}
          filter="url(#scoreGlow)"
          style={{
            transition: animated ? 'stroke-dashoffset 0.3s ease-out' : 'none'
          }}
        />
      </svg>
      
      <div className="score-circle__content">
        <span 
          className="score-circle__value"
          style={{ 
            fontSize: `${fontSize}px`,
            color: getScoreColor(normalizedScore)
          }}
        >
          {Math.round(score)}
        </span>
        {showLabel && label && (
          <span 
            className="score-circle__label"
            style={{ fontSize: `${labelSize}px` }}
          >
            {label}
          </span>
        )}
      </div>
    </div>
  );
  
  if (href) {
    return (
      <a 
        href={href} 
        target="_blank" 
        rel="noopener noreferrer"
        className="score-circle__link"
      >
        {content}
      </a>
    );
  }
  
  return content;
};

export default ScoreCircle;
