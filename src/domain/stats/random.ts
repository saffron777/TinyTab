/**
 * Generador pseudoaleatorio xoshiro128** con semilla explícita.
 * Determinista y documentado: no se usa Math.random en los cálculos.
 */
export class Rng {
  private state = new Uint32Array(4)
  private normalCache: number | null = null

  constructor(seed = 20261023) {
    this.seed(seed)
  }

  seed(seed: number): void {
    let x = seed >>> 0
    const splitmix = (): number => {
      x = (x + 0x9e3779b9) >>> 0
      let z = x
      z = Math.imul(z ^ (z >>> 16), 0x21f0aaad)
      z = Math.imul(z ^ (z >>> 15), 0x735a2d97)
      return (z ^ (z >>> 15)) >>> 0
    }
    for (let i = 0; i < 4; i += 1) this.state[i] = splitmix() || 1
    this.normalCache = null
  }

  /** Entero sin signo de 32 bits. */
  nextUint32(): number {
    const s = this.state
    const s0 = s[0] as number
    const s1 = s[1] as number
    const s2 = s[2] as number
    const s3 = s[3] as number
    const result = (Math.imul(rotl((Math.imul(s0 + s3, 5) >>> 0) + s0, 7), 9) >>> 0) as number
    const t = (s1 << 9) >>> 0
    s[2] = s2 ^ s0
    s[3] = s3 ^ s1
    s[1] = s1 ^ s[2]
    s[0] = s0 ^ s[3]
    s[2] = s[2] ^ t
    s[3] = rotl(s[3] as number, 11)
    return result >>> 0
  }

  /** Uniforme en [0, 1). */
  next(): number {
    return this.nextUint32() / 4294967296
  }

  uniform(lower = 0, upper = 1): number {
    return lower + (upper - lower) * this.next()
  }

  /** Normal estándar por Box-Muller con caché. */
  normal(): number {
    if (this.normalCache !== null) {
      const cached = this.normalCache
      this.normalCache = null
      return cached
    }
    let u = 0
    let v = 0
    while (u <= Number.EPSILON) u = this.next()
    v = this.next()
    const radius = Math.sqrt(-2 * Math.log(u))
    const angle = 2 * Math.PI * v
    this.normalCache = radius * Math.sin(angle)
    return radius * Math.cos(angle)
  }

  exponential(rate = 1): number {
    return -Math.log(1 - this.next()) / rate
  }

  /** Gamma(shape, scale=1) por Marsaglia-Tsang. */
  gamma(shape: number, scale = 1): number {
    if (shape < 1) {
      const u = this.next()
      return this.gamma(shape + 1, scale) * Math.pow(u, 1 / shape)
    }
    const d = shape - 1 / 3
    const c = 1 / Math.sqrt(9 * d)
    for (;;) {
      const x = this.normal()
      const v = Math.pow(1 + c * x, 3)
      if (v <= 0) continue
      const u = this.next()
      if (u < 1 - 0.0331 * x * x * x * x) return d * v * scale
      if (Math.log(u) < 0.5 * x * x + d * (1 - v + Math.log(v))) return d * v * scale
    }
  }

  beta(a: number, b: number): number {
    const x = this.gamma(a)
    const y = this.gamma(b)
    return x / (x + y)
  }

  weibull(shape: number, scale = 1): number {
    return scale * Math.pow(-Math.log(1 - this.next()), 1 / shape)
  }

  lognormal(location = 0, scale = 1): number {
    return Math.exp(location + scale * this.normal())
  }

  /** Binomial por inversión de la CDF (n pequeño) o aproximación normal. */
  binomial(n: number, p: number): number {
    if (n <= 0) return 0
    if (p <= 0) return 0
    if (p >= 1) return n
    if (n <= 200) {
      let probability = Math.pow(1 - p, n)
      const u = this.next()
      let cumulative = probability
      let k = 0
      while (u > cumulative && k < n) {
        k += 1
        probability *= ((n - k + 1) / k) * (p / (1 - p))
        cumulative += probability
      }
      return k
    }
    const meanValue = n * p
    const sdValue = Math.sqrt(n * p * (1 - p))
    return clamp(Math.round(meanValue + sdValue * this.normal()), 0, n)
  }

  /** Poisson por Knuth (λ pequeño) o aproximación normal. */
  poisson(lambda: number): number {
    if (lambda <= 0) return 0
    if (lambda < 30) {
      const limit = Math.exp(-lambda)
      let product = 1
      let k = 0
      do {
        k += 1
        product *= this.next()
      } while (product > limit && k < 10000)
      return k - 1
    }
    return Math.max(0, Math.round(lambda + Math.sqrt(lambda) * this.normal()))
  }

  sample<T>(items: readonly T[]): T {
    return items[Math.floor(this.next() * items.length)] as T
  }
}

function rotl(value: number, shift: number): number {
  return ((value << shift) | (value >>> (32 - shift))) >>> 0
}

function clamp(value: number, lower: number, upper: number): number {
  return Math.min(Math.max(value, lower), upper)
}
