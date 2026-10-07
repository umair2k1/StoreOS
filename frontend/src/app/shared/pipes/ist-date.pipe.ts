import { Pipe, PipeTransform } from '@angular/core';

const SHORT_MONTHS = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun',
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec'
];

@Pipe({
  name: 'istDate',
  standalone: true
})
export class PzDatePipe implements PipeTransform {
  transform(
    value: Date | string | number | null | undefined,
    format: string = 'DD-MMM-YYYY'
  ): string {
    if (value === null || value === undefined || value === '') {
      return '';
    }

    if (typeof value === 'string' && /^\d{4}-\d{2}-\d{2}$/.test(value)) {
      const [year, month, day] = value.split('-').map(Number);
      return this.formatParts(year, month - 1, day, 0, 0, 0, format);
    }

    const date = value instanceof Date ? value : new Date(value);
    if (Number.isNaN(date.getTime())) {
      return '';
    }

    return this.formatParts(
      date.getFullYear(),
      date.getMonth(),
      date.getDate(),
      date.getHours(),
      date.getMinutes(),
      date.getSeconds(),
      format
    );
  }

  private formatParts(
    year: number,
    monthIndex: number,
    day: number,
    hours: number,
    minutes: number,
    seconds: number,
    format: string
  ): string {
    const dd = String(day).padStart(2, '0');
    const mmm = SHORT_MONTHS[monthIndex] ?? '';
    const yyyy = String(year);
    const hh = String(hours).padStart(2, '0');
    const mm = String(minutes).padStart(2, '0');
    const ss = String(seconds).padStart(2, '0');

    if (format === 'DD-MMM-YYYY') {
      return `${dd}-${mmm}-${yyyy}`;
    }

    if (format === 'DD-MMM-YYYY HH:mm') {
      return `${dd}-${mmm}-${yyyy} ${hh}:${mm}`;
    }

    return format
      .replace('DD', dd)
      .replace('MMM', mmm)
      .replace('YYYY', yyyy)
      .replace('HH', hh)
      .replace('mm', mm)
      .replace('ss', ss);
  }
}

@Pipe({
  name: 'pzDate',
  standalone: true
})
export class PzDateAliasPipe extends PzDatePipe {}
