// @ts-check

export const MAX_HISTORY = 100;

/** @template T */
export class History {
    /** @type {T[]} */
    #states;
    #index = 0;

    /** @param {T} initial */
    constructor(initial) {
        this.#states = [initial];
    }

    /** @returns {T} */
    get current() {
        return this.#states[this.#index];
    }

    /** @returns {boolean} */
    get canUndo() {
        return this.#index > 0;
    }

    /** @returns {boolean} */
    get canRedo() {
        return this.#index < this.#states.length - 1;
    }

    /**
     * @param {T} state
     * @returns {void}
     */
    push(state) {
        this.#states = this.#states.slice(0, this.#index + 1);
        this.#states.push(state);
        if (this.#states.length > MAX_HISTORY) {
            this.#states.shift();
        }
        this.#index = this.#states.length - 1;
    }

    /** @returns {T} */
    undo() {
        if (this.canUndo) {
            this.#index--;
        }
        return this.current;
    }

    /** @returns {T} */
    redo() {
        if (this.canRedo) {
            this.#index++;
        }
        return this.current;
    }
}
